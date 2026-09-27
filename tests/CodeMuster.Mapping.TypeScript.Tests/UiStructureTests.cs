using CodeMuster.Domain;

namespace CodeMuster.Mapping.TypeScript.Tests;

public sealed class UiProject : IAsyncLifetime
{
    public static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>
    {
        ["site/tsconfig.json"] = """
            {
              "compilerOptions": { "strict": true, "allowJs": true, "jsx": "preserve", "noEmit": true, "moduleResolution": "bundler", "module": "esnext", "target": "es2022" }
            }
            """,
        ["site/components/Switch.tsx"] = """
            export function Switch({ label }: { label: string }) {
              return <button role="switch" aria-label={label} />;
            }
            """,
        ["site/components/Sidebar.jsx"] = """
            import Link from "next/link";

            const links = [
              { label: "Invoices", href: "/invoices" },
              { label: "Settings", href: "/settings" },
            ];

            export function Sidebar() {
              return (
                <nav>
                  {links.map((link) => (
                    <Link key={link.href} href={link.href}>{link.label}</Link>
                  ))}
                  <Link href="/reports">Reports</Link>
                  <a href="https://example.com/help">Help</a>
                </nav>
              );
            }
            """,
        ["site/app/settings/page.tsx"] = """
            import { Switch } from "../../components/Switch";

            export default function SettingsPage() {
              return (
                <main>
                  <h1>Settings</h1>
                  <section>
                    <h2>General</h2>
                    <label htmlFor="company">Company name</label>
                    <input id="company" name="company" />
                    <Switch label="Auto charge customer" />
                  </section>
                  <section>
                    <h2>Payments</h2>
                    <label>
                      Default currency
                      <select name="currency">
                        <option>USD</option>
                      </select>
                    </label>
                    <input type="checkbox" aria-label="Send receipts" />
                    <input type="hidden" name="token" />
                  </section>
                </main>
              );
            }
            """,
        ["site/app/invoices/page.tsx"] = """
            import Link from "next/link";

            export default function InvoicesPage() {
              return (
                <main>
                  <h1>Invoices</h1>
                  <Link href="/invoices/new">New invoice</Link>
                  <fieldset>
                    <legend>Filters</legend>
                    <textarea placeholder="Search notes" />
                  </fieldset>
                </main>
              );
            }
            """,
        ["site/pages/reports.tsx"] = """
            import { Sidebar } from "../components/Sidebar";

            export default Sidebar;
            """,
    };

    private readonly TempFolder _temp = new();

    public CodeMap Map { get; private set; } = new([], [], [], new ResolutionStats(0, 0, []), []);

    public async Task InitializeAsync()
    {
        _temp.Copy(TestPaths.TypeScriptPackage, "site/node_modules/typescript");
        foreach (var (path, content) in Files)
        {
            _temp.Write(path, content + "\n");
        }

        Map = await MapScript.RunAsync(_temp.Root, ["site/tsconfig.json"], [.. Files.Keys]);
    }

    public Task DisposeAsync()
    {
        _temp.Dispose();
        return Task.CompletedTask;
    }
}

public class UiStructureTests(UiProject project) : IClassFixture<UiProject>
{
    private static string Describe(UiElement element) =>
        $"{element.Path}:{element.Line} {element.Kind} \"{element.Text}\" control={element.Control} target={element.Target} section={element.Section} route={element.Route}";

    [Fact]
    public void The_settings_page_puts_auto_charge_under_general_and_the_payment_controls_under_payments()
    {
        Assert.Equal(
            [
                "site/app/settings/page.tsx:3 route \"/settings\" control= target= section= route=/settings",
                "site/app/settings/page.tsx:6 heading \"Settings\" control= target= section= route=/settings",
                "site/app/settings/page.tsx:8 heading \"General\" control= target= section=Settings route=/settings",
                "site/app/settings/page.tsx:10 control \"Company name\" control=text target= section=General route=/settings",
                "site/app/settings/page.tsx:11 control \"Auto charge customer\" control=Switch target= section=General route=/settings",
                "site/app/settings/page.tsx:14 heading \"Payments\" control= target= section=Settings route=/settings",
                "site/app/settings/page.tsx:17 control \"Default currency\" control=select target= section=Payments route=/settings",
                "site/app/settings/page.tsx:21 control \"Send receipts\" control=checkbox target= section=Payments route=/settings",
            ],
            project.Map.UiElements.Where(element => element.Path == "site/app/settings/page.tsx").Select(Describe));
    }

    [Fact]
    public void Navigation_comes_from_links_in_a_nav_and_from_arrays_of_label_and_href_objects()
    {
        Assert.Equal(
            [
                "site/components/Sidebar.jsx:4 nav \"Invoices\" control= target=/invoices section= route=",
                "site/components/Sidebar.jsx:5 nav \"Settings\" control= target=/settings section= route=",
                "site/components/Sidebar.jsx:14 nav \"Reports\" control= target=/reports section= route=",
                "site/components/Sidebar.jsx:15 nav \"Help\" control= target=https://example.com/help section= route=",
            ],
            project.Map.UiElements.Where(element => element.Path == "site/components/Sidebar.jsx").Select(Describe));
    }

    [Fact]
    public void A_link_outside_navigation_is_not_a_menu_entry_and_a_legend_heads_its_fieldset()
    {
        Assert.Equal(
            [
                "site/app/invoices/page.tsx:3 route \"/invoices\" control= target= section= route=/invoices",
                "site/app/invoices/page.tsx:6 heading \"Invoices\" control= target= section= route=/invoices",
                "site/app/invoices/page.tsx:9 heading \"Filters\" control= target= section=Invoices route=/invoices",
                "site/app/invoices/page.tsx:10 control \"Search notes\" control=textarea target= section=Filters route=/invoices",
            ],
            project.Map.UiElements.Where(element => element.Path == "site/app/invoices/page.tsx").Select(Describe));
        Assert.DoesNotContain(project.Map.UiElements, element => element.Path == "site/components/Switch.tsx");
    }

    [Fact]
    public void A_page_that_exports_a_component_from_another_file_has_its_route_on_its_own_export_line()
    {
        Assert.Equal(
            ["site/pages/reports.tsx:3 route \"/reports\" control= target= section= route=/reports"],
            project.Map.UiElements.Where(element => element.Path == "site/pages/reports.tsx").Select(Describe));
    }
}
