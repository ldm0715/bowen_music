using Bodian.Core.Models;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 手势文案。设置页的键位列与录制对话框都用它，写错了用户会以为键绑错了。
/// </summary>
public sealed class ShortcutGestureLabelTests
{
    [Theory]
    [InlineData(ShortcutKey.Left, ShortcutModifiers.Control, "Ctrl+←")]
    [InlineData(ShortcutKey.Right, ShortcutModifiers.Control, "Ctrl+→")]
    [InlineData(ShortcutKey.Up, ShortcutModifiers.Control, "Ctrl+↑")]
    [InlineData(ShortcutKey.Down, ShortcutModifiers.Control, "Ctrl+↓")]
    [InlineData(ShortcutKey.L, ShortcutModifiers.Control, "Ctrl+L")]
    [InlineData(ShortcutKey.Space, ShortcutModifiers.None, "空格")]
    [InlineData(ShortcutKey.F11, ShortcutModifiers.None, "F11")]
    [InlineData(ShortcutKey.L, ShortcutModifiers.Control | ShortcutModifiers.Alt, "Ctrl+Alt+L")]
    [InlineData(ShortcutKey.D5, ShortcutModifiers.Shift, "Shift+5")]
    [InlineData(ShortcutKey.OemMinus, ShortcutModifiers.Control, "Ctrl+-")]
    public void Format_ProducesTheExpectedText(ShortcutKey key, ShortcutModifiers modifiers, string expected) =>
        Assert.Equal(expected, ShortcutGestureLabel.Format(key, modifiers));

    [Fact]
    public void Format_KeepsAModifierOrderThatDoesNotDependOnHowItWasPressed()
    {
        // 修饰键是标志位组合，按位序输出会得到随机的先后。固定成 Ctrl/Alt/Shift。
        var one = ShortcutGestureLabel.Format(ShortcutKey.L, ShortcutModifiers.Control | ShortcutModifiers.Shift);
        var other = ShortcutGestureLabel.Format(ShortcutKey.L, ShortcutModifiers.Shift | ShortcutModifiers.Control);

        Assert.Equal("Ctrl+Shift+L", one);
        Assert.Equal(one, other);
    }

    [Fact]
    public void KeyName_IsNeverEmpty()
    {
        // 名字允许与枚举标识符相同（Tab / Home / A 这些本来就该显示成这样），
        // 断言只保证不会掉出空串。
        foreach (var value in Enum.GetValues<ShortcutKey>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ShortcutGestureLabel.KeyName(value)));
        }
    }

    [Fact]
    public void KeyName_UsesSymbolsForTheKeysThatHaveThem()
    {
        // 这几颗是「显示名与枚举标识符不同」的那一批，也是写错最明显的。
        Assert.Equal("←", ShortcutGestureLabel.KeyName(ShortcutKey.Left));
        Assert.Equal("↑", ShortcutGestureLabel.KeyName(ShortcutKey.Up));
        Assert.Equal("→", ShortcutGestureLabel.KeyName(ShortcutKey.Right));
        Assert.Equal("↓", ShortcutGestureLabel.KeyName(ShortcutKey.Down));
        Assert.Equal("空格", ShortcutGestureLabel.KeyName(ShortcutKey.Space));
        Assert.Equal("回车", ShortcutGestureLabel.KeyName(ShortcutKey.Enter));
        Assert.Equal("F1", ShortcutGestureLabel.KeyName(ShortcutKey.F1));
        Assert.Equal("F12", ShortcutGestureLabel.KeyName(ShortcutKey.F12));
        Assert.Equal("A", ShortcutGestureLabel.KeyName(ShortcutKey.A));
        Assert.Equal("Z", ShortcutGestureLabel.KeyName(ShortcutKey.Z));
        Assert.Equal("0", ShortcutGestureLabel.KeyName(ShortcutKey.D0));
        Assert.Equal("9", ShortcutGestureLabel.KeyName(ShortcutKey.D9));
    }
}
