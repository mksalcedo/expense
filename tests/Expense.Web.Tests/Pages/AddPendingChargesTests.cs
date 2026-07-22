using Bunit;
using Expense.Domain.Services.Categories;
using Expense.Domain.Services.Ingestion.ManualCharges;
using Expense.Web.Components.Pages;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Expense.Web.Tests.Pages;

public class AddPendingChargesTests : BunitContext
{
    private class FakeManualChargesPageProvider : IManualChargesPageProvider
    {
        public List<AccountOption> Accounts { get; set; } = [];
        public List<ManualChargeReviewRow> RowsToReturn { get; set; } = [];
        public int? LastReviewAccountId { get; private set; }
        public byte[]? LastReviewImageBytes { get; private set; }
        public int? LastAddAccountId { get; private set; }
        public List<ManualChargeReviewRow>? LastAddedRows { get; private set; }
        public Exception? ReviewException { get; set; }
        public Exception? AddException { get; set; }

        public Task<List<AccountOption>> GetActiveSpendingAccountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Accounts);

        public Task<List<ManualChargeReviewRow>> ReviewScreenshotAsync(
            int accountId, byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
        {
            if (ReviewException is not null) throw ReviewException;

            LastReviewAccountId = accountId;
            LastReviewImageBytes = imageBytes;
            return Task.FromResult(RowsToReturn);
        }

        public Task<int> AddChargesAsync(int accountId, List<ManualChargeReviewRow> rows, CancellationToken cancellationToken = default)
        {
            if (AddException is not null) throw AddException;

            LastAddAccountId = accountId;
            LastAddedRows = rows;
            return Task.FromResult(rows.Count);
        }
    }

    private class FakeJSStreamReference(byte[] bytes) : IJSStreamReference
    {
        public long Length => bytes.Length;

        public ValueTask<Stream> OpenReadStreamAsync(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream(bytes));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static FakeManualChargesPageProvider MakeProvider() => new()
    {
        Accounts = [new AccountOption { Id = 1, Name = "Amex" }]
    };

    private IRenderedComponent<AddPendingCharges> RenderPage()
    {
        var module = JSInterop.SetupModule("./js/addPendingCharges.js");
        module.SetupVoid("registerPasteListener", _ => true);
        module.SetupVoid("unregisterPasteListener", _ => true);

        return Render<AddPendingCharges>();
    }

    [Fact]
    public void RendersAccountDropdownOptions()
    {
        Services.AddSingleton<IManualChargesPageProvider>(MakeProvider());

        var cut = RenderPage();

        var options = cut.FindAll("#account-select option").Select(o => o.TextContent).ToList();
        Assert.Contains("Amex", options);
    }

    [Fact]
    public void WithOnlyOneAccount_ItIsPreselected()
    {
        Services.AddSingleton<IManualChargesPageProvider>(MakeProvider());

        var cut = RenderPage();

        var selected = cut.FindAll("#account-select option").Single(o => o.HasAttribute("selected"));
        Assert.Equal("Amex", selected.TextContent);
    }

    [Fact]
    public void WithMultipleAccounts_NoneIsPreselected()
    {
        var provider = new FakeManualChargesPageProvider
        {
            Accounts = [new AccountOption { Id = 1, Name = "Amex" }, new AccountOption { Id = 2, Name = "Other Card" }]
        };
        Services.AddSingleton<IManualChargesPageProvider>(provider);

        var cut = RenderPage();

        Assert.DoesNotContain(cut.FindAll("#account-select option"), o => o.HasAttribute("selected"));
    }

    [Fact]
    public void ParseButton_IsDisabled_UntilAccountAndFileAreBothSelected()
    {
        Services.AddSingleton<IManualChargesPageProvider>(MakeProvider());

        var cut = RenderPage();

        Assert.True(cut.Find("#parse-btn").HasAttribute("disabled"));

        cut.Find("#account-select").Change("1");
        Assert.True(cut.Find("#parse-btn").HasAttribute("disabled")); // still no file chosen

        var file = InputFileContent.CreateFromText("fake image bytes", "screenshot.png", contentType: "image/png");
        cut.FindComponent<InputFile>().UploadFiles(file);
        Assert.False(cut.Find("#parse-btn").HasAttribute("disabled"));
    }

    [Fact]
    public void SelectingAFile_ShowsAReadyLabel_NamingTheFile()
    {
        Services.AddSingleton<IManualChargesPageProvider>(MakeProvider());

        var cut = RenderPage();
        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("bytes", "screenshot.png", contentType: "image/png"));

        Assert.Contains("screenshot.png", cut.Find("#pending-image-label").TextContent);
    }

    [Fact]
    public async Task PastingAnImage_ShowsAReadyLabel_AndEnablesParse()
    {
        Services.AddSingleton<IManualChargesPageProvider>(MakeProvider());

        var cut = RenderPage();
        cut.Find("#account-select").Change("1");

        var pageInstance = cut.Instance;
        await cut.InvokeAsync(() => pageInstance.OnImagePasted(new FakeJSStreamReference([1, 2, 3]), "image/png"));

        Assert.Contains("pasted screenshot", cut.Find("#pending-image-label").TextContent);
        Assert.False(cut.Find("#parse-btn").HasAttribute("disabled"));
    }

    [Fact]
    public void ParsingAScreenshot_RendersNewRowsEditable_AndUnselectedDuplicateRowsHighlighted()
    {
        var provider = MakeProvider();
        provider.RowsToReturn =
        [
            new ManualChargeReviewRow { Date = new DateOnly(2026, 7, 20), Description = "MORGAN COMPOUDING", Amount = -131.65m, IsDuplicate = false },
            new ManualChargeReviewRow
            {
                Date = new DateOnly(2026, 7, 18), Description = "INGLES MARKETS", Amount = -171.95m, IsDuplicate = true,
                DuplicateReason = "Already in system - matches INGLES MARKETS #474 NORCROSS GA, $171.95, posted 07/18/2026"
            }
        ];
        Services.AddSingleton<IManualChargesPageProvider>(provider);

        var cut = RenderPage();
        cut.Find("#account-select").Change("1");
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("bytes", "s.png", contentType: "image/png"));
        cut.Find("#parse-btn").Click();

        var newRow = cut.Find("#review-row-0");
        Assert.DoesNotContain("background-color", newRow.GetAttribute("style") ?? "");
        Assert.True(cut.Find("#include-0").HasAttribute("checked"));

        var duplicateRow = cut.Find("#review-row-1");
        Assert.Contains("background-color", duplicateRow.GetAttribute("style"));
        Assert.Contains("posted 07/18/2026", duplicateRow.TextContent);
        Assert.False(cut.Find("#include-1").HasAttribute("checked"));

        // Checking the duplicate row back "in" (an explicit override) clears the highlight,
        // since the row will now actually be added.
        cut.Find("#include-1").Change(true);
        Assert.DoesNotContain("background-color", cut.Find("#review-row-1").GetAttribute("style") ?? "");
    }

    [Fact]
    public void ParseFailure_ShowsAnErrorMessage_AndRestoresTheButton()
    {
        var provider = MakeProvider();
        provider.ReviewException = new InvalidOperationException("Anthropic API request failed (400): Could not process image");
        Services.AddSingleton<IManualChargesPageProvider>(provider);

        var cut = RenderPage();
        cut.Find("#account-select").Change("1");
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("bytes", "s.png", contentType: "image/png"));
        cut.Find("#parse-btn").Click();

        Assert.Contains("Could not process image", cut.Find("#parse-error").TextContent);
        Assert.Equal("Parse Screenshot", cut.Find("#parse-btn").TextContent);
        Assert.False(cut.Find("#parse-btn").HasAttribute("disabled"));
    }

    [Fact]
    public async Task AddAll_OnlyCommitsIncludedRows()
    {
        var provider = MakeProvider();
        provider.RowsToReturn =
        [
            new ManualChargeReviewRow { Date = new DateOnly(2026, 7, 20), Description = "MORGAN COMPOUDING", Amount = -131.65m, IsDuplicate = false },
            new ManualChargeReviewRow { Date = new DateOnly(2026, 7, 18), Description = "INGLES MARKETS", Amount = -171.95m, IsDuplicate = true, DuplicateReason = "dup" }
        ];
        Services.AddSingleton<IManualChargesPageProvider>(provider);

        var cut = RenderPage();
        cut.Find("#account-select").Change("1");
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("bytes", "s.png", contentType: "image/png"));
        cut.Find("#parse-btn").Click();
        cut.Find("#add-all-btn").Click();

        Assert.Single(provider.LastAddedRows!);
        Assert.Equal("MORGAN COMPOUDING", provider.LastAddedRows![0].Description);
        Assert.Contains("Added 1 charge(s).", cut.Markup);
    }

    [Fact]
    public void AddAllFailure_ShowsAnErrorMessage_AndKeepsTheReviewTableForRetry()
    {
        var provider = MakeProvider();
        provider.RowsToReturn =
        [
            new ManualChargeReviewRow { Date = new DateOnly(2026, 7, 20), Description = "MORGAN COMPOUDING", Amount = -131.65m, IsDuplicate = false }
        ];
        provider.AddException = new InvalidOperationException("database is unavailable");
        Services.AddSingleton<IManualChargesPageProvider>(provider);

        var cut = RenderPage();
        cut.Find("#account-select").Change("1");
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("bytes", "s.png", contentType: "image/png"));
        cut.Find("#parse-btn").Click();
        cut.Find("#add-all-btn").Click();

        Assert.Contains("database is unavailable", cut.Find("#add-error").TextContent);
        Assert.NotNull(cut.Find("#review-table"));
    }

    [Fact]
    public void AddSelected_WithAnOverriddenDuplicateIncluded_ShowsAConfirmationModal_BeforeAdding()
    {
        var provider = MakeProvider();
        provider.RowsToReturn =
        [
            new ManualChargeReviewRow
            {
                Date = new DateOnly(2026, 7, 18), Description = "INGLES MARKETS", Amount = -171.95m, IsDuplicate = true,
                DuplicateReason = "Already in system - matches INGLES MARKETS #474, $171.95, posted 07/18/2026"
            }
        ];
        Services.AddSingleton<IManualChargesPageProvider>(provider);

        var cut = RenderPage();
        cut.Find("#account-select").Change("1");
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("bytes", "s.png", contentType: "image/png"));
        cut.Find("#parse-btn").Click();
        cut.Find("#include-0").Change(true);
        cut.Find("#add-all-btn").Click();

        Assert.Null(provider.LastAddedRows);
        var modal = cut.Find("#confirm-duplicates-modal");
        Assert.Contains("posted 07/18/2026", modal.TextContent);
    }

    [Fact]
    public void ConfirmingTheDuplicatesModal_ProceedsWithTheAdd()
    {
        var provider = MakeProvider();
        provider.RowsToReturn =
        [
            new ManualChargeReviewRow { Date = new DateOnly(2026, 7, 18), Description = "INGLES MARKETS", Amount = -171.95m, IsDuplicate = true, DuplicateReason = "dup" }
        ];
        Services.AddSingleton<IManualChargesPageProvider>(provider);

        var cut = RenderPage();
        cut.Find("#account-select").Change("1");
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("bytes", "s.png", contentType: "image/png"));
        cut.Find("#parse-btn").Click();
        cut.Find("#include-0").Change(true);
        cut.Find("#add-all-btn").Click();
        cut.Find("#confirm-add-duplicates-btn").Click();

        Assert.Single(provider.LastAddedRows!);
        Assert.False(cut.FindAll("#confirm-duplicates-modal").Count > 0);
    }

    [Fact]
    public void CancelingTheDuplicatesModal_AddsNothing_AndKeepsTheReviewTable()
    {
        var provider = MakeProvider();
        provider.RowsToReturn =
        [
            new ManualChargeReviewRow { Date = new DateOnly(2026, 7, 18), Description = "INGLES MARKETS", Amount = -171.95m, IsDuplicate = true, DuplicateReason = "dup" }
        ];
        Services.AddSingleton<IManualChargesPageProvider>(provider);

        var cut = RenderPage();
        cut.Find("#account-select").Change("1");
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("bytes", "s.png", contentType: "image/png"));
        cut.Find("#parse-btn").Click();
        cut.Find("#include-0").Change(true);
        cut.Find("#add-all-btn").Click();
        cut.Find("#cancel-add-duplicates-btn").Click();

        Assert.Null(provider.LastAddedRows);
        Assert.False(cut.FindAll("#confirm-duplicates-modal").Count > 0);
        Assert.NotNull(cut.Find("#review-table"));
    }

    [Fact]
    public void AddSelected_WithNoDuplicatesIncluded_SkipsTheModal()
    {
        var provider = MakeProvider();
        provider.RowsToReturn =
        [
            new ManualChargeReviewRow { Date = new DateOnly(2026, 7, 20), Description = "MORGAN COMPOUDING", Amount = -131.65m, IsDuplicate = false }
        ];
        Services.AddSingleton<IManualChargesPageProvider>(provider);

        var cut = RenderPage();
        cut.Find("#account-select").Change("1");
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("bytes", "s.png", contentType: "image/png"));
        cut.Find("#parse-btn").Click();
        cut.Find("#add-all-btn").Click();

        Assert.Single(provider.LastAddedRows!);
        Assert.False(cut.FindAll("#confirm-duplicates-modal").Count > 0);
    }
}
