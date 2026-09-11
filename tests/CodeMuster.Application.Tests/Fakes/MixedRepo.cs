using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public static class MixedRepo
{
    public const string ControllerPath = "src/MixedRepo.Api/Controllers/QuotesController.cs";
    public const string MigrationPath = "src/MixedRepo.Api/Data/Migrations/20260101000000_Initial.cs";
    public const string QuotePath = "src/MixedRepo.Api/Data/Quote.cs";
    public const string RepositoryPath = "src/MixedRepo.Api/Data/QuoteRepository.cs";
    public const string ProgramPath = "src/MixedRepo.Api/Program.cs";
    public const string InterfacePath = "src/MixedRepo.Api/Services/IQuoteService.cs";
    public const string ServicePath = "src/MixedRepo.Api/Services/QuoteService.cs";
    public const string MoneyPath = "src/MixedRepo.Api/Shared/Money.cs";
    public const string WorkerPath = "src/MixedRepo.Api/Workers/ReminderWorker.cs";
    public const string CustomersPagePath = "web/app/customers/page.tsx";
    public const string QuotesPagePath = "web/app/quotes/page.tsx";
    public const string QuoteTablePath = "web/components/QuoteTable.tsx";
    public const string UseQuotesPath = "web/hooks/useQuotes.ts";
    public const string ApiPath = "web/lib/api.ts";
    public const string BarrelPath = "web/lib/index.ts";
    public const string PackageJsonPath = "web/package.json";

    public const string ControllerListQuotes = "M:MixedRepo.Api.Controllers.QuotesController.ListQuotes(System.Int32)";
    public const string ControllerGetQuote = "M:MixedRepo.Api.Controllers.QuotesController.GetQuote(System.Int32,System.Int32)";
    public const string RepositoryConstructor = "M:MixedRepo.Api.Data.QuoteRepository.#ctor";
    public const string ListForTenant = "M:MixedRepo.Api.Data.QuoteRepository.ListForTenant(System.Int32)";
    public const string FindForTenant = "M:MixedRepo.Api.Data.QuoteRepository.FindForTenant(System.Int32,System.Int32)";
    public const string ServiceListQuotes = "M:MixedRepo.Api.Services.QuoteService.ListQuotes(System.Int32)";
    public const string ServiceGetQuote = "M:MixedRepo.Api.Services.QuoteService.GetQuote(System.Int32,System.Int32)";
    public const string ArchiveQuote = "M:MixedRepo.Api.Services.QuoteService.ArchiveQuote(System.Int32)";
    public const string ToSummary = "M:MixedRepo.Api.Services.QuoteService.ToSummary(MixedRepo.Api.Data.Quote)";
    public const string MoneyFormat = "M:MixedRepo.Api.Shared.Money.Format(System.Decimal)";
    public const string ExecuteAsync = "M:MixedRepo.Api.Workers.ReminderWorker.ExecuteAsync(System.Threading.CancellationToken)";
    public const string MigrationUp = "M:MixedRepo.Api.Data.Migrations.Initial.Up";
    public const string CustomersPage = "web/app/customers/page.tsx#CustomersPage";
    public const string QuotesPage = "web/app/quotes/page.tsx#QuotesPage";
    public const string QuoteTable = "web/components/QuoteTable.tsx#QuoteTable";
    public const string UseQuotes = "web/hooks/useQuotes.ts#useQuotes";
    public const string FetchQuotes = "web/lib/api.ts#fetchQuotes";
    public const string FetchCustomers = "web/lib/api.ts#fetchCustomers";

    public static readonly IReadOnlyList<string> TrackedPaths =
    [
        ".gitignore",
        "Directory.Build.props",
        "MixedRepo.sln",
        ControllerPath,
        MigrationPath,
        QuotePath,
        "src/MixedRepo.Api/Data/QuoteDto.g.cs",
        RepositoryPath,
        "src/MixedRepo.Api/MixedRepo.Api.csproj",
        ProgramPath,
        InterfacePath,
        ServicePath,
        MoneyPath,
        WorkerPath,
        CustomersPagePath,
        QuotesPagePath,
        QuoteTablePath,
        UseQuotesPath,
        ApiPath,
        BarrelPath,
        "web/package-lock.json",
        PackageJsonPath,
        "web/tsconfig.json",
        "web/types/react.d.ts",
    ];

    private const string ControllerHeader = "[ApiController] public sealed class QuotesController(IQuoteService quotes) : ControllerBase\n";
    private const string RepositoryHeader = "public sealed class QuoteRepository\n";
    private const string ServiceHeader = "public sealed class QuoteService(QuoteRepository repository) : IQuoteService\n";

    public static CodeMap CSharp() => new(
        [
            Method(ControllerListQuotes, ControllerPath, 9, 13, ControllerHeader + "[HttpGet(\"quotes\")] public IReadOnlyList<QuoteSummary> ListQuotes(int tenantId)"),
            Method(ControllerGetQuote, ControllerPath, 15, 25, ControllerHeader + "[HttpGet(\"quotes/{id}\")] public ActionResult<QuoteSummary> GetQuote(int tenantId, int id)"),
            Method(MigrationUp, MigrationPath, 5, 7, "public sealed class Initial\npublic void Up()"),
            Method(RepositoryConstructor, RepositoryPath, 7, 15, RepositoryHeader + "public QuoteRepository()"),
            Method(ListForTenant, RepositoryPath, 17, 20, RepositoryHeader + "public IReadOnlyList<Quote> ListForTenant(int tenantId)"),
            Method(FindForTenant, RepositoryPath, 22, 25, RepositoryHeader + "public Quote? FindForTenant(int tenantId, int id)"),
            Method(ServiceListQuotes, ServicePath, 8, 11, ServiceHeader + "public IReadOnlyList<QuoteSummary> ListQuotes(int tenantId)"),
            Method(ServiceGetQuote, ServicePath, 13, 17, ServiceHeader + "public QuoteSummary? GetQuote(int tenantId, int id)"),
            Method(ArchiveQuote, ServicePath, 19, 22, ServiceHeader + "public string ArchiveQuote(int id)"),
            Method(ToSummary, ServicePath, 24, 27, ServiceHeader + "private static QuoteSummary ToSummary(Quote quote)"),
            Method(MoneyFormat, MoneyPath, 7, 10, "public static class Money\npublic static string Format(decimal amount)"),
            Method(ExecuteAsync, WorkerPath, 7, 15, "public sealed class ReminderWorker(IQuoteService quotes, ILogger<ReminderWorker> logger) : BackgroundService\nprotected override async Task ExecuteAsync(CancellationToken stoppingToken)"),
        ],
        [
            new Edge(ControllerListQuotes, ServiceListQuotes, EdgeKind.Bound),
            new Edge(ControllerGetQuote, ServiceGetQuote, EdgeKind.Bound),
            new Edge(ServiceListQuotes, ListForTenant, EdgeKind.Call),
            new Edge(ServiceListQuotes, ToSummary, EdgeKind.Call),
            new Edge(ServiceGetQuote, FindForTenant, EdgeKind.Call),
            new Edge(ServiceGetQuote, ToSummary, EdgeKind.Call),
            new Edge(ToSummary, MoneyFormat, EdgeKind.Call),
            new Edge(ExecuteAsync, ServiceListQuotes, EdgeKind.Bound),
        ],
        [
            new EntryPoint(ControllerListQuotes, "http", "GET /quotes"),
            new EntryPoint(ControllerGetQuote, "http", "GET /quotes/{id}"),
            new EntryPoint(ExecuteAsync, "background", "ReminderWorker"),
        ],
        new ResolutionStats(17, 0, []),
        []);

    public static CodeMap TypeScript() => new(
        [
            Function(CustomersPage, CustomersPagePath, 4, 19, "export default function CustomersPage()"),
            Function(QuotesPage, QuotesPagePath, 4, 12, "export default function QuotesPage()"),
            Function(QuoteTable, QuoteTablePath, 3, 17, "export function QuoteTable({ quotes }: { quotes: Quote[] })"),
            Function(UseQuotes, UseQuotesPath, 4, 10, "export function useQuotes(tenantId: number): Quote[]"),
            Function(FetchQuotes, ApiPath, 8, 11, "export async function fetchQuotes(tenantId: number): Promise<Quote[]>"),
            Function(FetchCustomers, ApiPath, 13, 16, "export async function fetchCustomers(): Promise<string[]>"),
        ],
        [
            new Edge(CustomersPage, FetchCustomers, EdgeKind.Call),
            new Edge(QuotesPage, UseQuotes, EdgeKind.Call),
            new Edge(QuotesPage, QuoteTable, EdgeKind.Call),
            new Edge(UseQuotes, FetchQuotes, EdgeKind.Call),
        ],
        [
            new EntryPoint(QuotesPage, "page", "/quotes"),
            new EntryPoint(CustomersPage, "page", "/customers"),
        ],
        new ResolutionStats(11, 0, []),
        []);

    public static IReadOnlyList<FileRecord> Included() =>
        TrackedPaths
            .Where(path => Exclusions.Reason(path, false) is null)
            .Select(path => new FileRecord(path, Languages.FromPath(path), "content:" + path, 1, "2026-09-10T00:00:00.0000000Z", "2026-09-10T12:00:00.0000000Z", "2026-09-10T12:00:00.0000000Z", null, null, null, null, null, null))
            .ToList();

    public static void AddTo(FakeSourceTree tree)
    {
        foreach (var path in TrackedPaths)
        {
            tree.Add(path, "content of " + path);
        }
    }

    private static Symbol Method(string id, string path, int start, int end, string signature) =>
        new(id, path, new LineRange(start, end), "method", signature, "body:" + id);

    private static Symbol Function(string id, string path, int start, int end, string signature) =>
        new(id, path, new LineRange(start, end), "function", signature, "body:" + id);
}
