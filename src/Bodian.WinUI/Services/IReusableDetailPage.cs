namespace Bodian.WinUI.Services;

internal interface IReusableDetailPage { }

internal interface IReusableDetailPage<in TModel> : IReusableDetailPage
{
    void Rebind(TModel model);
}
