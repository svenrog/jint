using Jint.Runtime;

namespace Jint.Tests.Runtime;

// Covers the two public reset points that let an Engine be reused across independent runs without
// leaking per-run state: Engine.Modules.Clear() and Engine.Advanced.ClearGlobalLexicalDeclarations().
public class EngineReuseTests
{
    [Fact]
    public void ModulesClear_AllowsReimportingSameSpecifierWithFreshSource()
    {
        var engine = new Engine();

        engine.Modules.Add("chunk", "export const value = 1;");
        Assert.Equal(1, engine.Modules.Import("chunk").Get("value").AsInteger());

        engine.Modules.Clear();

        engine.Modules.Add("chunk", "export const value = 2;");
        Assert.Equal(2, engine.Modules.Import("chunk").Get("value").AsInteger());
    }

    [Fact]
    public void ModulesClear_ReevaluatesModuleTopLevelSideEffects()
    {
        var engine = new Engine();
        var evaluations = 0;
        engine.SetValue("mark", () => evaluations++);

        void AddAndImport()
        {
            engine.Modules.Add("chunk", "mark(); export const value = 1;");
            engine.Modules.Import("chunk");
        }

        AddAndImport();
        AddAndImport();
        // Second import resolves the cached module, so its top-level code does not run again.
        Assert.Equal(1, evaluations);

        engine.Modules.Clear();
        AddAndImport();
        Assert.Equal(2, evaluations);
    }

    [Fact]
    public void ClearGlobalLexicalDeclarations_AllowsRedeclaringGlobalLetConstClass()
    {
        var engine = new Engine();
        const string script = "let token = 1; const NAME = 'a'; class Widget {}";

        engine.Execute(script);
        Assert.Throws<JavaScriptException>(() => engine.Execute(script));

        engine.Advanced.ClearGlobalLexicalDeclarations();

        engine.Execute("let token = 2; const NAME = 'b'; class Widget {}");
        Assert.Equal(2, engine.Evaluate("token").AsInteger());
    }

    [Fact]
    public void ClearGlobalLexicalDeclarations_LeavesGlobalObjectPropertiesIntact()
    {
        var engine = new Engine();
        engine.Execute("var kept = 'survivor'; globalThis.alsoKept = 42; let dropped = 1;");

        engine.Advanced.ClearGlobalLexicalDeclarations();

        Assert.Equal("survivor", engine.Evaluate("kept").AsString());
        Assert.Equal(42, engine.Evaluate("alsoKept").AsInteger());
        // The lexical `let` is gone, so re-declaring it must not throw.
        engine.Execute("let dropped = 2;");
        Assert.Equal(2, engine.Evaluate("dropped").AsInteger());
    }

    [Fact]
    public void ClearGlobalLexicalDeclarations_InvalidatesCachedIdentifierResolution()
    {
        var engine = new Engine();

        // A prepared script keeps cached identifier resolution across runs. The clear must bump the global
        // lexical version so a name that was unresolved on the first run re-resolves against a binding
        // created afterwards, rather than reading a stale cached slot.
        var prepared = Engine.PrepareScript("(typeof marker === 'undefined') ? 'absent' : marker");

        Assert.Equal("absent", engine.Evaluate(in prepared).AsString());

        engine.Advanced.ClearGlobalLexicalDeclarations();
        engine.SetValue("marker", "present");

        Assert.Equal("present", engine.Evaluate(in prepared).AsString());
    }
}
