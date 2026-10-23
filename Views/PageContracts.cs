namespace ShiJi.Views;

public interface IRefreshablePage
{
    void RefreshData();
    void OnTick();
}

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}
