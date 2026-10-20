// SOURCE ONLY / NOT BUILT OR RUN. Plumbing-only child fixture for an existing
// bounded test host. These bytes are not valid rendered PDFs or product acceptance.
// No shell, arbitrary commands, process creation, deletion or overwrites outside
// explicitly selected disposable fixture paths. Root pins every fixture input.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class GenericAdapterFixture
{
    public const string Build = "synthetic-adapter-fixture";
    public const string Profile = "{\"schema\":\"pdfform.profile/3\",\"id\":\"fictional-adapter-fixture\",\"revision\":\"1\",\"profileVersion\":1,\"keys\":[]}";
    public const string Catalog = "{\"schema\":\"fictional-adapter-catalog/1\"}";
    public const string Blank = "%PDF-1.7\n% FICTIONAL ADAPTER PLUMBING ONLY\n";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    // Deliberate valid-vocabulary canary: only later sample discovery must reveal this synthetic leak.
    private const string LateValue = "configuration.assessment";
    private static readonly string[] Known = ["good", "wrong-check-template", "staged-config", "save-exit4-published", "mutate-reviewed-after-save", "wrong-document-path", "mutate-sample-after-editable", "mutate-earlier-pdf-after-flattened", "nested-value-field", "warning-value-leak", "escaped-warning-value-leak", "late-discovered-value-leak", "no-source-draft-cut", "draft-provenance-mismatch", "unrelated-reviewed-copy", "wrong-check-state"];
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static byte[] Encode(object value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    private static void New(string path, byte[] bytes) { using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); file.Write(bytes); }
    private static object Descriptor(string id, string path, byte[] bytes) => new { id, path, bytes = bytes.Length, sha256 = Hash(bytes) };
    private static string Option(string[] args, string name)
    { int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : throw new InvalidOperationException(); }
    private static Dictionary<string, string> Levels(bool configuration, bool bound = true, bool rendered = false) => new()
    { ["syntax"]="passed", ["profile"]="passed", ["binding"]=bound?"passed":"not_run", ["bindingStructure"]=bound?"passed":"not_run", ["data"]=configuration?"not_run":"passed", ["packet"]="not_run", ["render"]=rendered?"passed":"not_run" };
    private static object Result(string operation, byte[] template, object[] outputs) => new
    { schema="pdfform.result/2", operation, status="ok", exitCode=0, artifactState="published", validationLevels=Levels(false,rendered:operation=="fill"),
      tool=new { name="synthetic-adapter-fixture", version="source-only", build=Build, engine="none", runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, sourceReference="fixture" },
      profiles=Array.Empty<object>(), templates=new[]{new{id="template",sha256=Hash(template)}}, outputs, errors=Array.Empty<object>(), warnings=Array.Empty<object>(), stats=new Dictionary<string,long>(), filingReadiness="not_assessed" };
    private static object Configuration(string operation, byte[] template, byte[] profile, object[] outputs) => new
    { schema="pdfform.configuration-result/1", operation, status="ok", exitCode=0, artifactState="published", validationLevels=Levels(true,operation=="save-config"),
      template=new{path="template.pdf",bytes=template.Length,sha256=Hash(template)},
      profile=new{path="profile.json",bytes=profile.Length,sha256=Hash(profile),id="fictional-adapter-fixture",revision="1",profileVersion=1},
      outputs, errors=Array.Empty<object>(), warnings=Array.Empty<object>(), filingReadiness="not_assessed" };
    public static int Main(string[] args)
    {
        try
        {
            string operation=args[0];
            string templatePath=operation=="sample-input"
                ? Path.Combine(new FileInfo(Option(args,"--catalog")).Directory!.Parent!.Parent!.Parent!.FullName,"fixtures","third-form.pdf") : args[1];
            byte[] template=File.ReadAllBytes(templatePath);
            string scenarioPath=Path.Combine(Path.GetDirectoryName(templatePath)!,"adapter-fixture-case.txt");
            using var scenarioStream=new FileStream(scenarioPath,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(scenarioStream.Length is < 1 or > 128) return 4;
            byte[] scenarioBytes=new byte[(int)scenarioStream.Length];scenarioStream.ReadExactly(scenarioBytes);
            string scenario=new UTF8Encoding(false,true).GetString(scenarioBytes);
            if(!Known.Contains(scenario,StringComparer.Ordinal))return 4;
            if(operation=="check")
            {
                Console.WriteLine(JsonSerializer.Serialize(new{schema="pdfform.compatibility/1",status="configuration_needed",exitCode=1,
                    templateSha256=scenario=="wrong-check-template"?new string('0',64):Hash(template),templateBytes=template.Length,
                    counts=new{pages=1L,terminalFields=0L,textFields=0L,checkboxFields=0L,otherFields=0L,widgets=0L,requiredFields=0L,combFields=0L},configurationState=scenario=="wrong-check-state"?"reviewed":"absent",
                    issues=scenario=="late-discovered-value-leak"?new object[]{new{code="W_COMPATIBILITY_ASSESSMENT",capability=LateValue}}:Array.Empty<object>(),
                    missingEngineFeatures=Array.Empty<string>(),requiredConfiguration=new[]{"operatorReview"},notAssessed=new[]{"viewerAcceptance"}},Json));
                return 1;
            }
            string output=Option(args,"--out");
            if(operation is "configure" or "save-config")
            {
                if(Directory.Exists(output)||File.Exists(output))return 2;Directory.CreateDirectory(output);
                byte[] profile=operation=="configure"?Encoding.UTF8.GetBytes(Profile):File.ReadAllBytes(Path.Combine(Option(args,"--draft"),"profile.json"));
                byte[] catalog=operation=="configure"?Encoding.UTF8.GetBytes(Catalog):File.ReadAllBytes(Path.Combine(Option(args,"--draft"),"catalog.json"));
                New(Path.Combine(output,"profile.json"),profile);New(Path.Combine(output,"catalog.json"),catalog);
                var artifacts=new List<object>{Descriptor("catalog","catalog.json",catalog),Descriptor("profile","profile.json",profile)};
                if(operation=="save-config"){New(Path.Combine(output,"template.pdf"),template);artifacts.Add(Descriptor("template","template.pdf",template));}
                byte[] result=Encode(Configuration(operation,template,profile,artifacts.ToArray()));New(Path.Combine(output,"result.json"),result);
                New(Path.Combine(output,"completion.json"),Encode(new{schema="pdfform.completion/1",requestId=Guid.NewGuid().ToString("N"),toolBuild=Build,
                    result=new{path="result.json",bytes=result.Length,sha256=Hash(result)},artifacts}));
                var terminal=JsonNode.Parse(result)!.AsObject();int exit=0;
                if(scenario=="staged-config"&&operation=="configure")terminal["artifactState"]="staged";
                if(scenario=="save-exit4-published"&&operation=="save-config"){terminal["status"]="internal_error";terminal["exitCode"]=exit=4;}
                if(scenario=="mutate-reviewed-after-save"&&operation=="save-config")File.AppendAllText(Path.Combine(Option(args,"--draft"),"profile.json")," ");
                Console.WriteLine(terminal.ToJsonString(Json));return exit;
            }
            if(operation=="sample-input")
            {
                byte[] data=Encode(new{schema="pdfform.data/3",profile="fictional-adapter-fixture@1",values=new{participant=scenario=="late-discovered-value-leak"?LateValue:"FICTIONAL MAPLE MAKER"}});
                New(output,data);Console.WriteLine(JsonSerializer.Serialize(Result(operation,template,[Descriptor("data","data.json",data)]),Json));return 0;
            }
            if(operation=="fill")
            {
                _=File.ReadAllBytes(Option(args,"--data"));bool flat=args.Contains("--flatten",StringComparer.Ordinal);
                byte[] pdf=Encoding.ASCII.GetBytes("%PDF-1.7\n% FICTIONAL "+(flat?"FLATTENED":"EDITABLE")+" PLUMBING ONLY\n");New(output,pdf);
                if(scenario=="mutate-sample-after-editable"&&!flat)File.AppendAllText(Option(args,"--data")," ");
                if(scenario=="mutate-earlier-pdf-after-flattened"&&flat)File.AppendAllText(Path.Combine(Path.GetDirectoryName(output)!,"editable.pdf"),"changed");
                var receipt=JsonSerializer.SerializeToNode(Result(operation,template,[Descriptor("document",scenario=="wrong-document-path"?"different.pdf":"document.pdf",pdf)]),Json)!.AsObject();
                if(scenario=="nested-value-field")receipt["tool"]!["values"]=JsonSerializer.SerializeToNode(new{participant="FICTIONAL MAPLE MAKER"},Json);
                if(scenario is "warning-value-leak" or "escaped-warning-value-leak")receipt["warnings"]=new JsonArray(new JsonObject
                { ["code"]="W_CONFIGURATION_ASSESSMENT",["stage"]="binding",["message"]="FICTIONAL MAPLE MAKER",["severity"]="warning" });
                string wire=receipt.ToJsonString(Json);
                if(scenario=="escaped-warning-value-leak")wire=wire.Replace("FICTIONAL MAPLE MAKER","F\\u0049CTIONAL MAPLE MAKER",StringComparison.Ordinal);
                Console.WriteLine(wire);return 0;
            }
            return 2;
        }
        catch { Console.Error.WriteLine("E_SYNTHETIC_ADAPTER_FIXTURE");return 4; }
    }
}
