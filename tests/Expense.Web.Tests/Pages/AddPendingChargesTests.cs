using Bunit;
using Expense.Domain.Services.Categories;
using Expense.Domain.Services.Ingestion.ManualCharges;
using Expense.Web.Components.Pages;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

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

        public Task<List<AccountOption>> GetActiveSpendingAccountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Accounts);

        public Task<List<ManualChargeReviewRow>> ReviewScreenshotAsync(
            int accountId, byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
        {
            LastReviewAccountId = accountId;
            LastReviewImageBytes = imageBytes;
            return Task.FromResult(RowsToReturn);
        }

        public Task<int> AddChargesAsync(int accountId, List<ManualChargeReviewRow> rows, CancellationToken cancellationToken = default)
        {
            LastAddAccountId = accountId;
            LastAddedRows = rows;
            return Task.FromResult(rows.Count);
        }
    }

    private static FakeManualChargesPageProvider MakeProvider() => new()
    {
        Accounts = [new AccountOption { Id = 1, Name = "Amex" }]
    };

    [Fact]
    public void RendersAccountDropdownOptions()
    {
        Services.AddSingleton<IManualChargesPageProvider>(MakeProvider());

        var cut = Render<AddPendingCharges>();

        var options = cut.FindAll("#account-select option").Select(o => o.TextContent).ToList();
        Assert.Contains("Amex", options);
    }

    [Fact]
    public void ParseButton_IsDisabled_UntilAccountAndFileAreBothSelected()
    {
        Services.AddSingleton<IManualChargesPageProvider>(MakeProvider());

        var cut = Render<AddPendingCharges>();

        Assert.True(cut.Find("#parse-btn").HasAttribute("disabled"));

        cut.Find("#account-select").Change("1");
        Assert.True(cut.Find("#parse-btn").HasAttribute("disabled")); // still no file chosen

        var file = InputFileContent.CreateFromText("fake image bytes", "screenshot.png", contentType: "image/png");
        cut.FindComponent<InputFile>().UploadFiles(file);
        Assert.False(cut.Find("#parse-btn").HasAttribute("disabled"));
    }

    [Fact]
    public void ParsingAScreenshot_RendersNewRowsEditable_AndDuplicateRowsStruckThroughWithReason()
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

        var cut = Render<AddPendingCharges>();
        cut.Find("#account-select").Change("1");
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("bytes", "s.png", contentType: "image/png"));
        cut.Find("#parse-btn").Click();

        var newRow = cut.Find("#review-row-0");
        Assert.DoesNotContain("line-through", newRow.GetAttribute("style"));
        Assert.True(cut.Find("#include-0").HasAttribute("checked"));

        var duplicateRow = cut.Find("#review-row-1");
        Assert.Contains("line-through", duplicateRow.GetAttribute("style"));
        Assert.Contains("posted 07/18/2026", duplicateRow.TextContent);
        Assert.False(cut.Find("#include-1").HasAttribute("checked"));
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

        var cut = Render<AddPendingCharges>();
        cut.Find("#account-select").Change("1");
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("bytes", "s.png", contentType: "image/png"));
        cut.Find("#parse-btn").Click();
        cut.Find("#add-all-btn").Click();

        Assert.Single(provider.LastAddedRows!);
        Assert.Equal("MORGAN COMPOUDING", provider.LastAddedRows![0].Description);
        Assert.Contains("Added 1 charge(s).", cut.Markup);
    }

    [Fact]
    public void AddAll_IncludesAnOverriddenDuplicateRow_WhenManuallyChecked()
    {
        var provider = MakeProvider();
        provider.RowsToReturn =
        [
            new ManualChargeReviewRow { Date = new DateOnly(2026, 7, 18), Description = "INGLES MARKETS", Amount = -171.95m, IsDuplicate = true, DuplicateReason = "dup" }
        ];
        Services.AddSingleton<IManualChargesPageProvider>(provider);

        var cut = Render<AddPendingCharges>();
        cut.Find("#account-select").Change("1");
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("bytes", "s.png", contentType: "image/png"));
        cut.Find("#parse-btn").Click();
        cut.Find("#include-0").Change(true);
        cut.Find("#add-all-btn").Click();

        Assert.Single(provider.LastAddedRows!);
    }
}
