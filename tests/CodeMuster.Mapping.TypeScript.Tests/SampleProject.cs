using CodeMuster.Domain;

namespace CodeMuster.Mapping.TypeScript.Tests;

public sealed class SampleProject : IAsyncLifetime
{
    public const string ExcludedPath = "site/generated/client.ts";

    public static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>
    {
        ["site/tsconfig.json"] = """
            {
              "compilerOptions": {
                "strict": true,
                "jsx": "preserve",
                "noEmit": true,
                "moduleResolution": "bundler",
                "module": "esnext",
                "target": "es2022"
              }
            }
            """,
        ["site/services/quotes.ts"] = """
            export class Repo {
              /** Loads one quote. */
              load(id: number): number {
                return id;
              }
            }

            export class QuoteService {
              constructor(private readonly repo: Repo) {}

              get(id: number): number {
                return this.repo.load(id);
              }
            }

            export const format = (value: number) => value.toFixed(2);

            export const parse = function (text: string) {
              return Number(text);
            };

            export let counter = () => 0;

            export function build() {
              return new QuoteService(new Repo());
            }

            build();
            """,
        ["site/components/Widget.tsx"] = """
            export class Widget {
              static Panel() {
                return <aside />;
              }
            }
            """,
        ["site/generated/client.ts"] = """
            export function generated() {
              return 1;
            }
            """,
        ["site/pages/index.tsx"] = """
            import { Widget } from "../components/Widget";

            export default function () {
              return <Widget.Panel />;
            }
            """,
        ["site/pages/customers/[id].tsx"] = """
            import { format, parse } from "../../services/quotes";
            import { generated } from "../../generated/client";

            const CustomerPage = () => {
              const values = ["1", "2"].map(parse);
              const render = () => values.map((value) => format(value));
              generated();
              missingHelper();
              missingHelper();
              return <section>{render()}</section>;
            };

            export default CustomerPage;
            """,
        ["site/pages/_app.tsx"] = """
            export default function App() {
              return null;
            }
            """,
        ["site/pages/_document.tsx"] = """
            export default function Document() {
              return null;
            }
            """,
        ["site/pages/_error.tsx"] = """
            export default function ErrorPage() {
              return null;
            }
            """,
        ["site/pages/api/health.ts"] = """
            export default function handler() {
              return 1;
            }
            """,
        ["site/app/(marketing)/quotes/[id]/page.tsx"] = """
            declare const legacy: any;

            export default function QuotePage() {
              legacy.boot();
              alpha();
              return <main />;
            }
            """,
        ["site/app/page.tsx"] = """
            export default () => <main />;
            """,
    };

    private readonly TempFolder _temp = new();

    public CodeMap Map { get; private set; } = new([], [], [], new ResolutionStats(0, 0, []), []);

    public async Task InitializeAsync()
    {
        _temp.Copy(Path.Combine(TestPaths.MixedRepoWithTypeScript(), "web", "node_modules"), "site/node_modules");
        foreach (var (path, content) in Files)
        {
            _temp.Write(path, content + "\n");
        }

        var paths = Files.Keys.Where(path => path != ExcludedPath).ToList();
        Map = await MapScript.RunAsync(_temp.Root, ["site/tsconfig.json"], paths);
    }

    public Task DisposeAsync()
    {
        _temp.Dispose();
        return Task.CompletedTask;
    }
}
