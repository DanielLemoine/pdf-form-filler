using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using PdfForm.Cli;

string root = Path.GetFullPath(args.Single());
string temp = Path.Combine(root, "evidence", "cli", "inputs-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
string profile = Path.Combine(root,"profiles","irs-941-2026-03.json");
string data = Path.Combine(root,"samples","941-2026q3-semiweekly.json");
string template = Path.Combine(root,"fixtures","appearance","forms","f941.pdf");
int passed=0,failed=0;
void Assert(bool value){if(!value)throw new InvalidOperationException("Assertion failed");}
void Test(string name,Action body){try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+" ["+ex.GetType().Name+"]");}}
string FileOf(string name,string text){string p=Path.Combine(temp,name);File.WriteAllText(p,text,new UTF8Encoding(false));return p;}
JsonNode Result(CliExecution value)=>JsonNode.Parse(value.Text)!;
void AssertError(CliExecution value,int exit,string code){Assert(value.ExitCode==exit);Assert(Result(value)["errors"]!.AsArray().Any(x=>x!["code"]!.ToString()==code));}
Test("help states incomplete scope",()=>{var r=CliApplication.Execute(["--help"]);Assert(r.ExitCode==0&&r.Text.Contains("not yet enabled",StringComparison.Ordinal));});
Test("offline version",()=>{var r=CliApplication.Execute(["--version"]);Assert(r.ExitCode==0&&Result(r)["engine"]!.ToString()=="iText 9.8.0");});
Test("unknown command no echo",()=>{var r=CliApplication.Execute(["PRIVATE_SENTINEL_COMMAND"]);AssertError(r,2,"E_COMMAND_UNSUPPORTED");Assert(!r.Text.Contains("PRIVATE_SENTINEL",StringComparison.Ordinal));});
foreach(string command in new[]{"run","read","diff","describe","template"})Test("explicit unsupported "+command,()=>AssertError(CliApplication.Execute([command]),2,"E_COMMAND_UNSUPPORTED"));
Test("fill requires explicit inputs",()=>AssertError(CliApplication.Execute(["fill"]),2,"E_ARGUMENTS"));
Test("duplicate option",()=>AssertError(CliApplication.Execute(["validate","--profile",profile,"--profile",profile,"--data",data]),2,"E_ARGUMENTS"));
Test("missing option value",()=>AssertError(CliApplication.Execute(["validate","--profile",profile,"--data"]),2,"E_ARGUMENTS"));
Test("unsupported file publication",()=>AssertError(CliApplication.Execute(["validate","--profile",profile,"--data",data,"--result",Path.Combine(temp,"result.json")]),2,"E_OUTPUT_UNSUPPORTED"));
Test("no-pdf validation levels",()=>{var r=CliApplication.Execute(["validate","--profile",profile,"--data",data]);Assert(r.ExitCode==0);var j=Result(r);Assert(j["validationLevels"]!["binding"]!.ToString()=="not_run"&&j["artifactState"]!.ToString()=="none"&&j["filingReadiness"]!.ToString()=="not_assessed");});
Test("private source paths not reported",()=>{var r=CliApplication.Execute(["validate","--profile",Path.Combine(temp,"PRIVATE_SENTINEL_FILE.json"),"--data",data]);AssertError(r,2,"E_INPUT_FILE");Assert(!r.Text.Contains(temp,StringComparison.Ordinal)&&!r.Text.Contains("PRIVATE_SENTINEL",StringComparison.Ordinal));});
Test("malformed profile exit3",()=>AssertError(CliApplication.Execute(["validate","--profile",FileOf("bad-profile.json","{"),"--data",data]),3,"E_PROFILE_JSON"));
Test("empty profile exit3",()=>AssertError(CliApplication.Execute(["validate","--profile",FileOf("empty-profile.json",""),"--data",data]),3,"E_PROFILE_JSON"));
Test("malformed data precedes malformed profile",()=>{var r=CliApplication.Execute(["validate","--profile",FileOf("profile-invalid.json","{"),"--data",FileOf("data-invalid.json","{")]);AssertError(r,2,"E_INPUT_JSON");});
Test("bad profile precedes data rules",()=>{var d=JsonNode.Parse(File.ReadAllText(data))!;d["values"]!["name"]="";var r=CliApplication.Execute(["validate","--profile",FileOf("profile-invalid2.json","{"),"--data",FileOf("data-missing-name.json",d.ToJsonString())]);Assert(r.ExitCode==3);});
Test("data semantic validation exit1",()=>{var d=JsonNode.Parse(File.ReadAllText(data))!;d["values"]!["name"]="";AssertError(CliApplication.Execute(["validate","--profile",profile,"--data",FileOf("missing-name.json",d.ToJsonString())]),1,"E_REQUIRED");});
Test("verbose privacy",()=>{var d=JsonNode.Parse(File.ReadAllText(data))!;d["values"]!["line2.wages"]="PRIVATE_SENTINEL_MONEY";d["values"]!["PRIVATE_SENTINEL_KEY"]="PRIVATE_SENTINEL_VALUE";var r=CliApplication.Execute(["validate","--profile",profile,"--data",FileOf("sensitive-invalid.json",d.ToJsonString()),"--verbose"]);Assert(r.ExitCode==1&&!r.Text.Contains("PRIVATE_SENTINEL",StringComparison.Ordinal));});
Test("known template inspect inventory",()=>{var r=CliApplication.Execute(["inspect",template]);Assert(r.ExitCode==0&&Result(r)["terminalFieldCount"]!.GetValue<int>()==116);Assert(!r.Text.Contains("actualTitle",StringComparison.Ordinal));});
Test("static check-profile does not claim full binding",()=>{var r=CliApplication.Execute(["check-profile",template,profile]);Assert(r.ExitCode==0);var j=Result(r);Assert(j["validationLevels"]!["bindingStructure"]!.ToString()=="passed"&&j["validationLevels"]!["binding"]!.ToString()=="not_run");Assert(j["warnings"]!.AsArray().Any(x=>x!["code"]!.ToString()=="W_RENDER_CHECKS_NOT_RUN"));});
Test("template mismatch precedes data rules",()=>{string changed=Path.Combine(temp,"changed.pdf");File.WriteAllBytes(changed,File.ReadAllBytes(template).Concat(new byte[]{10}).ToArray());var d=JsonNode.Parse(File.ReadAllText(data))!;d["values"]!["name"]="";var r=CliApplication.Execute(["validate","--profile",profile,"--data",FileOf("invalid-data3.json",d.ToJsonString()),"--pdf",changed]);Assert(r.ExitCode==3&&Result(r)["status"]!.ToString()=="mismatch");});
Test("malformed data precedes template mismatch",()=>{string changed=Path.Combine(temp,"changed2.pdf");File.WriteAllBytes(changed,File.ReadAllBytes(template).Concat(new byte[]{10}).ToArray());var r=CliApplication.Execute(["validate","--profile",profile,"--data",FileOf("malformed-data4.json","{"),"--pdf",changed]);Assert(r.ExitCode==2);});
Test("writer failure safe stderr",()=>{var err=new StringWriter();int exit=CliApplication.Run(["--help"],new BrokenWriter(),err);Assert(exit==4&&err.ToString().Trim()=="E_DIAGNOSTIC_WRITE");});
Test("unregistered profile identifiers stay out of diagnostics",()=>{var p=JsonNode.Parse(File.ReadAllText(profile))!;p["keys"]![2]!["key"]="PRIVATE_SENTINEL_PROFILE_KEY";p["keys"]![2]!["required"]=true;var r=CliApplication.Execute(["validate","--profile",FileOf("unreviewed-profile.json",p.ToJsonString()),"--data",data]);Assert(r.ExitCode==1&&!r.Text.Contains("PRIVATE_SENTINEL",StringComparison.Ordinal));Assert(Result(r)["profiles"]!.AsArray().Count==0);});
Test("oversized known-role profile exit3",()=>{var oversized=FileOf("oversized-profile.json",new string(' ',PdfForm.Core.Limits.MaxJsonBytes+1));AssertError(CliApplication.Execute(["validate","--profile",oversized,"--data",data]),3,"E_JSON_SIZE");});
Test("oversized data exit2",()=>{var oversized=FileOf("oversized-data.json",new string(' ',PdfForm.Core.Limits.MaxJsonBytes+1));AssertError(CliApplication.Execute(["validate","--profile",profile,"--data",oversized]),2,"E_JSON_SIZE");});
Test("data syntax precedes profile size category",()=>{var oversized=Path.Combine(temp,"oversized-profile.json");AssertError(CliApplication.Execute(["validate","--profile",oversized,"--data",FileOf("malformed-data5.json","{")]),2,"E_INPUT_JSON");});
Test("inaccessible input precedes profile size category",()=>{var oversized=Path.Combine(temp,"oversized-profile.json");AssertError(CliApplication.Execute(["validate","--profile",oversized,"--data",Path.Combine(temp,"missing-data.json")]),2,"E_INPUT_FILE");});
string[] FillArgs(string inputProfile,string inputData,string name,bool flatten=false)=>new[]{"fill",template,"--profile",inputProfile,"--data",inputData,"--out",Path.Combine(temp,name)}.Concat(flatten?new[]{"--flatten"}:Array.Empty<string>()).ToArray();
Test("fill force remains explicitly unqualified",()=>AssertError(CliApplication.Execute(FillArgs(profile,data,"force.pdf").Append("--force").ToArray()),3,"E_FORCE_UNQUALIFIED"));
Test("fill duplicate flatten rejected",()=>AssertError(CliApplication.Execute(FillArgs(profile,data,"duplicate.pdf",true).Append("--flatten").ToArray()),2,"E_ARGUMENTS"));
Test("fill data syntax precedes malformed profile",()=>AssertError(CliApplication.Execute(FillArgs(FileOf("fill-bad-profile.json","{"),FileOf("fill-bad-data.json","{"),"malformed.pdf")),2,"E_INPUT_JSON"));
Test("fill unregistered profile fails without exposing identifiers",()=>{var p=JsonNode.Parse(File.ReadAllText(profile))!;p["keys"]![2]!["key"]="PRIVATE_SENTINEL_FILL_KEY";var r=CliApplication.Execute(FillArgs(FileOf("fill-unregistered.json",p.ToJsonString()),data,"unregistered.pdf"));AssertError(r,3,"E_PROFILE_NOT_RELEASED");Assert(!r.Text.Contains("PRIVATE_SENTINEL",StringComparison.Ordinal)&&!File.Exists(Path.Combine(temp,"unregistered.pdf")));});
Test("fill invalid values do not publish or echo values",()=>{var d=JsonNode.Parse(File.ReadAllText(data))!;d["values"]!["name"]="";d["values"]!["PRIVATE_SENTINEL_FIELD"]="PRIVATE_SENTINEL_VALUE";var r=CliApplication.Execute(FillArgs(profile,FileOf("fill-invalid-data.json",d.ToJsonString()),"invalid.pdf"));AssertError(r,1,"E_REQUIRED");Assert(!r.Text.Contains("PRIVATE_SENTINEL",StringComparison.Ordinal)&&!File.Exists(Path.Combine(temp,"invalid.pdf")));});
Test("fill input failure precedes oversized profile",()=>AssertError(CliApplication.Execute(FillArgs(Path.Combine(temp,"oversized-profile.json"),Path.Combine(temp,"missing-data.json"),"missing.pdf")),2,"E_PATH_ACCESS"));
Test("fill output collision precedes invalid data",()=>{var d=JsonNode.Parse(File.ReadAllText(data))!;d["values"]!["name"]="";var r=CliApplication.Execute(["fill",template,"--profile",profile,"--data",FileOf("fill-invalid-collision.json",d.ToJsonString()),"--out",profile]);AssertError(r,2,"E_PATH_COLLISION");});
Test("fill existing destination precedes invalid profile",()=>{string name="already-exists.pdf";FileOf(name,"ORIGINAL");var r=CliApplication.Execute(FillArgs(FileOf("fill-existing-bad-profile.json","{"),data,name));AssertError(r,2,"E_DESTINATION_EXISTS");Assert(File.ReadAllText(Path.Combine(temp,name))=="ORIGINAL");});
foreach(bool flatten in new[]{false,true})Test("fill creates verified new output "+flatten,()=>{string name=flatten?"verified-flat.pdf":"verified-editable.pdf";var r=CliApplication.Execute(FillArgs(profile,data,name,flatten));Assert(r.ExitCode==0);var j=Result(r);Assert(j["artifactState"]!.ToString()=="published"&&j["filingReadiness"]!.ToString()=="not_assessed"&&j["validationLevels"]!["binding"]!.ToString()=="passed"&&j["validationLevels"]!["packet"]!.ToString()=="not_run");Assert(File.Exists(Path.Combine(temp,name))&&j["outputs"]!.AsArray().Count==1);});
Test("standalone destination basename stays private on success and failure",()=>{const string name="PRIVATE_SENTINEL_DESTINATION.pdf";var arguments=FillArgs(profile,data,name);var stdout=new StringWriter();var stderr=new StringWriter();Assert(CliApplication.Run(arguments,stdout,stderr)==0);Assert(!stdout.ToString().Contains("PRIVATE_SENTINEL",StringComparison.Ordinal)&&stderr.ToString()=="");var j=JsonNode.Parse(stdout.ToString())!;var artifact=j["outputs"]![0]!;byte[] bytes=File.ReadAllBytes(Path.Combine(temp,name));Assert(artifact["id"]!.ToString()=="document"&&artifact["path"]!.ToString()=="document.pdf"&&artifact["bytes"]!.GetValue<long>()==bytes.LongLength&&artifact["sha256"]!.ToString()==Convert.ToHexStringLower(SHA256.HashData(bytes)));stdout.GetStringBuilder().Clear();Assert(CliApplication.Run(arguments,stdout,stderr)==2);Assert(!stdout.ToString().Contains("PRIVATE_SENTINEL",StringComparison.Ordinal)&&stderr.ToString()==""&&bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(temp,name))));});
Test("broken stdout after fill preserves and reports committed artifact",()=>{const string name="PRIVATE_SENTINEL_STDOUT.pdf";var stderr=new StringWriter();Assert(CliApplication.Run(FillArgs(profile,data,name),new BrokenWriter(),stderr)==4);Assert(File.Exists(Path.Combine(temp,name))&&new FileInfo(Path.Combine(temp,name)).Length>0);Assert(stderr.ToString().Trim()=="E_DIAGNOSTIC_WRITE artifactState=published"&&!stderr.ToString().Contains("PRIVATE_SENTINEL",StringComparison.Ordinal));});
Test("both broken diagnostic channels cannot roll back committed fill",()=>{const string name="both-writers-failed.pdf";Assert(CliApplication.Run(FillArgs(profile,data,name),new BrokenWriter(),new BrokenWriter())==4);Assert(File.Exists(Path.Combine(temp,name)));});
// Exercise the unchanged caller-facing Schedule B sample, not the renderer's renamed positive fixture.
foreach (bool flatten in new[] { false, true }) Test("fill stock Schedule B output and no-overwrite " + (flatten ? "flattened" : "editable"), () =>
{
    string scheduleProfile = Path.Combine(root, "profiles", "irs-941sb-2024-03.json");
    string scheduleData = Path.Combine(root, "samples", "941sb-2026q3.json");
    string scheduleTemplate = Path.Combine(root, "fixtures", "appearance", "forms", "f941sb.pdf");
    string[] inputs = [scheduleProfile, scheduleData, scheduleTemplate];
    var inputHashes = inputs.ToDictionary(path => path, path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);
    Assert(inputHashes[scheduleData] == "0b7c2bdc932152fc904f809a19722c12ca6f2f4afc245e60bebb99d79ecd407a");
    Assert(JsonNode.Parse(File.ReadAllBytes(scheduleData))!["values"]!["name"]!.ToString() == "SAMPLE CITY OF EXAMPLEVILLE");
    string name = flatten ? "PRIVATE_SENTINEL_SCHEDULE_B_FLAT.pdf" : "PRIVATE_SENTINEL_SCHEDULE_B_EDITABLE.pdf";
    string outputPath = Path.Combine(temp, name);
    string[] arguments = new[] { "fill", scheduleTemplate, "--profile", scheduleProfile, "--data", scheduleData, "--out", outputPath }
        .Concat(flatten ? new[] { "--flatten" } : Array.Empty<string>()).ToArray();
    var stdout = new StringWriter();
    var stderr = new StringWriter();
    Assert(CliApplication.Run(arguments, stdout, stderr) == 0);
    Assert(stderr.ToString() == "");
    string firstText = stdout.ToString();
    Assert(!firstText.Contains("PRIVATE_SENTINEL", StringComparison.Ordinal) &&
        !firstText.Contains("SAMPLE CITY OF EXAMPLEVILLE", StringComparison.Ordinal) &&
        !firstText.Contains(temp, StringComparison.Ordinal));
    var result = JsonNode.Parse(firstText)!;
    Assert(result["exitCode"]!.GetValue<int>() == 0 && result["artifactState"]!.ToString() == "published" && result["filingReadiness"]!.ToString() == "not_assessed");
    Assert(result["validationLevels"]!["binding"]!.ToString() == "passed" &&
        result["validationLevels"]!["render"]!.ToString() == "passed" &&
        result["validationLevels"]!["packet"]!.ToString() == "not_run");
    Assert(result["errors"]!.AsArray().Count == 0);
    string[] warningCodes = result["warnings"]!.AsArray().Select(item => item!["code"]!.ToString()).ToArray();
    Assert(warningCodes.Contains("W_FONT_EXPLICIT_REPLACEMENT", StringComparer.Ordinal) &&
        !warningCodes.Contains("W_FONT_UNEMBEDDED_PORTABILITY_UNQUALIFIED", StringComparer.Ordinal) &&
        warningCodes.Contains("W_ACCEPTANCE_NOT_COMPLETE", StringComparer.Ordinal));
    Assert(result["outputs"]!.AsArray().Count == 1);
    var artifact = result["outputs"]![0]!;
    byte[] originalOutput = File.ReadAllBytes(outputPath);
    string outputHash = Convert.ToHexStringLower(SHA256.HashData(originalOutput));
    Assert(originalOutput.Length > 0 && artifact["id"]!.ToString() == "document" && artifact["path"]!.ToString() == "document.pdf" &&
        artifact["bytes"]!.GetValue<long>() == originalOutput.LongLength && artifact["sha256"]!.ToString() == outputHash);
    DateTime originalWriteTime = File.GetLastWriteTimeUtc(outputPath);
    string[] originalEntries = Directory.GetFileSystemEntries(temp).OrderBy(path => path, StringComparer.Ordinal).ToArray();

    // Repeat the identical argument array against the committed output. It must fail before any replacement.
    stdout.GetStringBuilder().Clear();
    Assert(CliApplication.Run(arguments, stdout, stderr) == 2);
    Assert(stderr.ToString() == "");
    string repeatText = stdout.ToString();
    Assert(!repeatText.Contains("PRIVATE_SENTINEL", StringComparison.Ordinal) &&
        !repeatText.Contains("SAMPLE CITY OF EXAMPLEVILLE", StringComparison.Ordinal) &&
        !repeatText.Contains(temp, StringComparison.Ordinal));
    var repeat = JsonNode.Parse(repeatText)!;
    Assert(repeat["exitCode"]!.GetValue<int>() == 2 && repeat["errors"]!.AsArray().Count == 1 &&
        repeat["errors"]![0]!["code"]!.ToString() == "E_DESTINATION_EXISTS");
    Assert(repeat["artifactState"]!.ToString() == "none" && repeat["outputs"]!.AsArray().Count == 0);
    byte[] preservedOutput = File.ReadAllBytes(outputPath);
    Assert(originalOutput.SequenceEqual(preservedOutput) && preservedOutput.LongLength == originalOutput.LongLength &&
        Convert.ToHexStringLower(SHA256.HashData(preservedOutput)) == outputHash && File.GetLastWriteTimeUtc(outputPath) == originalWriteTime);
    Assert(originalEntries.SequenceEqual(Directory.GetFileSystemEntries(temp).OrderBy(path => path, StringComparer.Ordinal)));
    foreach (string input in inputs)
        Assert(inputHashes[input] == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(input))));
});
Console.WriteLine($"RESULT passed={passed} failed={failed}");return failed==0?0:1;
sealed class BrokenWriter:TextWriter{public override Encoding Encoding=>Encoding.UTF8;public override void WriteLine(string? value)=>throw new IOException("PRIVATE_EXCEPTION_SENTINEL");}
