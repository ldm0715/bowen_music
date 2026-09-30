namespace Bodian.Core.Api;

public sealed class BodianSessionClearedEventArgs : EventArgs
{
    public BodianSessionClearedEventArgs(string reason) => Reason = reason;

    public string Reason { get; }
}

/// <summary>
/// 当前会话。uid / token 的持有者，以及「会话被清」这件事的唯一来源。
/// </summary>
/// <remarks>
/// <para>
/// 线程安全：所有读写都在锁内。<see cref="Revision"/> 单调递增，用来识别「迟到的响应」。
/// </para>
/// <para>
/// 清会话语义收敛在这里：传输层拿到 <c>11012</c> 只负责调 <see cref="Clear"/>，
/// 不自己判断该不该登出。这样「11012 → 会话被清」与「11012 → 抛出正确错误码」
/// 两件事可以分别单测。
/// </para>
/// </remarks>
public sealed class BodianSession
{
    /// <summary>未登录时的 uid。传输层据此决定要不要带身份请求头。</summary>
    public const string AnonymousUid = "-1";

    private readonly Lock _gate = new();
    private string _uid = AnonymousUid;
    private string _token = string.Empty;
    private int _revision;

    /// <summary>
    /// 造一个未登录会话。
    /// </summary>
    /// <remarks>
    /// 这里给的是**工厂**而不是 <c>static</c> 共享实例——共享的可变单例会让一处登出
    /// 影响所有使用方，那种 bug 很难查。
    /// </remarks>
    public static BodianSession CreateAnonymous() => new();

    public string Uid
    {
        get
        {
            lock (_gate)
            {
                return _uid;
            }
        }
    }

    public string Token
    {
        get
        {
            lock (_gate)
            {
                return _token;
            }
        }
    }

    public bool IsAuthenticated
    {
        get
        {
            lock (_gate)
            {
                return _uid != AnonymousUid && _uid.Length > 0;
            }
        }
    }

    /// <summary>
    /// 变更计数。每次登录 / 登出 / 被服务端清会话都自增。
    /// </summary>
    /// <remarks>
    /// <b>用法</b>：发请求前记下当前值，响应回来后对比 <see cref="BodianEnvelope{T}.SessionRevision"/>。
    /// 若已变化，说明期间发生了登录态变更，**应当丢弃这次的结果**——
    /// 否则「切号后迟到的旧请求」会往新会话的界面上写数据。
    /// </remarks>
    public int Revision
    {
        get
        {
            lock (_gate)
            {
                return _revision;
            }
        }
    }

    /// <summary>会话被清时触发。<b>不要在回调里做阻塞 IO。</b></summary>
    public event EventHandler<BodianSessionClearedEventArgs>? Cleared;

    public void Set(string uid, string token)
    {
        ArgumentNullException.ThrowIfNull(uid);
        ArgumentNullException.ThrowIfNull(token);

        lock (_gate)
        {
            _uid = uid;
            _token = token;
            _revision++;
        }
    }

    /// <summary>登出。已经是匿名状态时是空操作（不会无谓地推高 <see cref="Revision"/>）。</summary>
    public void Clear() => ClearInternal("本地登出");

    /// <summary>
    /// 服务端说这个会话无效了（业务码 <c>11012</c>）。由传输层调用。
    /// </summary>
    internal void NotifyUnauthorized() => ClearInternal("服务端要求重新鉴权（11012）");

    private void ClearInternal(string reason)
    {
        lock (_gate)
        {
            if (_uid == AnonymousUid && _token.Length == 0)
            {
                return;
            }

            _uid = AnonymousUid;
            _token = string.Empty;
            _revision++;
        }

        Cleared?.Invoke(this, new BodianSessionClearedEventArgs(reason));
    }
}
