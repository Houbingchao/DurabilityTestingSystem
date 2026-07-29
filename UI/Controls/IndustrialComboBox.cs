namespace DurabilityTestingSystem.UI.Controls;

/// <summary>
/// 避免可编辑式 ComboBox 在页面首次显示或切换页面后保留蓝色全选状态。
/// </summary>
public sealed class IndustrialComboBox : ComboBox
{
    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        ClearTextSelection();
        ClearSelectionDeferred();
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        ClearTextSelection();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (!Focused) ClearTextSelection();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) ClearSelectionDeferred();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        ClearSelectionDeferred();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        ClearTextSelection();
        base.OnLostFocus(e);
    }

    public void ClearTextSelection()
    {
        if (DropDownStyle != ComboBoxStyle.DropDown || IsDisposed) return;
        SelectionStart = Text.Length;
        SelectionLength = 0;
    }

    private void ClearSelectionDeferred()
    {
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke(new Action(ClearTextSelection));
    }
}
