using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Services;
using OpenKh.Tools.ModsManager.UserControls;
using System.Linq;
using Xunit;

namespace OpenKh.Tests.ModsManager;

public class AvaloniaDialogMappingTests
{
    [Theory]
    [InlineData(MessageDialogButtons.Ok, MessageDialogResult.Ok)]
    [InlineData(MessageDialogButtons.OkCancel, MessageDialogResult.Cancel)]
    [InlineData(MessageDialogButtons.YesNo, MessageDialogResult.No)]
    [InlineData(MessageDialogButtons.YesNoCancel, MessageDialogResult.Cancel)]
    public void CloseResultUsesPredictableCancelButton(MessageDialogButtons buttons, MessageDialogResult expected) =>
        Assert.Equal(expected, AvaloniaMessageDialogMapping.CloseResult(buttons));

    [Theory]
    [InlineData(MessageDialogButtons.Ok, "OK", "OK")]
    [InlineData(MessageDialogButtons.OkCancel, "OK", "Cancel")]
    [InlineData(MessageDialogButtons.YesNo, "Yes", "No")]
    [InlineData(MessageDialogButtons.YesNoCancel, "Yes", "Cancel")]
    public void ButtonMappingPreservesDefaultAndCancel(
        MessageDialogButtons buttons, string defaultCaption, string cancelCaption)
    {
        var mapping = AvaloniaMessageDialogMapping.Buttons(buttons);

        Assert.Equal(defaultCaption, Assert.Single(mapping.Where(x => x.IsDefault)).Caption);
        Assert.Equal(cancelCaption, Assert.Single(mapping.Where(x => x.IsCancel)).Caption);
    }

    [Fact]
    public void FilterParserPreservesWildcardPatternsExactly()
    {
        var filters = SaveFileSelectorControl.ParseFilters(
            "Archives|*.zip;*.kh2pcpatch|All files|*");

        Assert.Equal(new[] { "*.zip", "*.kh2pcpatch" }, filters[0].Patterns);
        Assert.Equal(new[] { "*" }, filters[1].Patterns);
    }

    [Fact]
    public void AvaloniaTypeMappingDoesNotTreatPatternsAsExtensions()
    {
        var types = AvaloniaFilePickerService.Types(new[]
        {
            new FilePickerFilter("Mixed", new[] { "*.zip", "archive.*", "*" }),
        });

        Assert.Equal(new[] { "*.zip", "archive.*", "*" }, types[0].Patterns);
    }
}
