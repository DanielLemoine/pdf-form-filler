using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdfForm.Core;

string root = args.Length == 1 ? Path.GetFullPath(args[0]) : throw new ArgumentException("Provide fixture root");
int passed = 0, failed = 0;
void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + " [" + ex.GetType().Name + "]"); }
}
void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed"); }
byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());
JsonNode Profile(string id = "941") => JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "profiles", id == "941" ? "irs-941-2026-03.json" : "irs-941sb-2024-03.json")))!;
JsonNode Data(string sample = "941-2026q3-semiweekly") => JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "samples", sample + ".json")))!;
ProfileDefinition ParseP(JsonNode node) { var result = ProfileParser.ParseProfile(Bytes(node)); Assert(result.IsValid); return result.Value!; }
ValidationReport Validate(JsonNode data, JsonNode? profile = null)
{
    var p = ParseP(profile ?? Profile(data["profile"]!.ToString().StartsWith("irs-941sb@", StringComparison.Ordinal) ? "941sb" : "941"));
    var d = DataValidator.ParseData(Bytes(data)); Assert(d.IsValid); return DataValidator.Validate(p, d.Value!);
}
void Has(ValidationReport report, string code) => Assert(report.Findings.Any(x => x.Code == code));
void No(ValidationReport report, string code) => Assert(!report.Findings.Any(x => x.Code == code));
JsonObject Values(JsonNode data) => data["values"]!.AsObject();
JsonObject Key(JsonNode profile, string name) => profile["keys"]!.AsArray().Select(x => x!.AsObject()).Single(x => x["key"]!.ToString() == name);
void BadProfile(string name, Action<JsonNode> mutate, string? code = null) => Test(name, () => { var p = Profile(); mutate(p); var result = ProfileParser.ParseProfile(Bytes(p)); Assert(!result.IsValid); if (code is not null) Assert(result.Findings.Any(x => x.Code == code)); });
void BadData(string name, string key, JsonNode? value, string code) => Test(name, () => { var d = Data(); Values(d)[key] = value; var parse = DataValidator.ParseData(Bytes(d)); if (!parse.IsValid) Assert(parse.Findings.Any(x => x.Code == code)); else Has(Validate(d), code); });

foreach (string sample in new[] { "941-2026q3-semiweekly", "941-2026q3-monthly-final-refund", "941sb-2026q3" })
    Test("valid migrated sample " + sample, () => Assert(Validate(Data(sample)).IsValid));
Test("invalid original has independent categories", () => { var r=Validate(Data("941-invalid-examples")); foreach(var c in new[]{"E_UNKNOWN_KEY","E_REQUIRED","E_MONEY","E_MONEY_NEGATIVE","E_CHECKBOX","E_DATE","E_OPTION","E_SPLIT_LENGTH","E_PATTERN","E_REQUIRED_IF"}) Has(r,c); });
foreach (var pair in new[] { ("941",68,116), ("941sb",101,212) })
    Test("preserved ordered map " + pair.Item1, () =>
    {
        var p = ParseP(Profile(pair.Item1)); Assert(p.Keys.Length == pair.Item2); Assert(p.EnumerateTargets().Count() == pair.Item3);
        var expected = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root,"tests/PdfForm.Core.Tests",p.Id+".expected-mapping.json")))!.AsArray();
        for(int i=0;i<p.Keys.Length;i++){Assert(p.Keys[i].Key==expected[i]!["key"]!.ToString()); Assert(p.Keys[i].EnumerateTargets().SequenceEqual(expected[i]!["targets"]!.AsArray().Select(x=>x!.ToString())));}
    });
Test("BOM accepted", () => Assert(DataValidator.ParseData(new byte[]{239,187,191}.Concat(Bytes(Data())).ToArray()).IsValid));
Test("invalid UTF8 rejected", () => Assert(!DataValidator.ParseData(new byte[]{123,34,120,34,58,34,255,34,125}).IsValid));
foreach(var json in new[]{"{\"schema\":\"a\",\"schema\":\"b\"}","{\"x\":{\"a\":1,\"a\":2}}","{\"x\":[{\"a\":1,\"a\":2}]}","{\"x\":{\"a\":1,\"\\u0061\":2}}"})
    Test("duplicate JSON nesting "+passed,()=>Assert(StrictJson.Parse<DataDocument>(Encoding.UTF8.GetBytes(json),"data").Findings.Any(x=>x.Code=="E_JSON_DUPLICATE")));
foreach(var json in new[]{"{}{}","{/*comment*/}","{\"x\":1,}","{\"x\":\"\\uD800\"}","{\"x\":\"\\uDC00\"}","{\"x\":null}"})
    Test("strict JSON syntax "+passed,()=>Assert(!StrictJson.Parse<DataDocument>(Encoding.UTF8.GetBytes(json),"data").IsValid));
Test("depth cap",()=>Assert(!StrictJson.Parse<DataDocument>(Encoding.UTF8.GetBytes(new string('[',33)+new string(']',33)),"data").IsValid));
Test("size cap",()=>Assert(StrictJson.Parse<DataDocument>(new byte[Limits.MaxJsonBytes+1],"data").Findings.Any(x=>x.Code=="E_JSON_SIZE")));
Test("unknown data envelope",()=>{var d=Data();d["comment"]="not supported";Assert(!DataValidator.ParseData(Bytes(d)).IsValid);});
Test("Rev1 rejected",()=>{var d=Data();d["schema"]="pdfform.data/1";Assert(!DataValidator.ParseData(Bytes(d)).IsValid);});
foreach (var value in new[]{"1e3","1,000.00","+1"," 1","1 ","$1","1.000","１２.００","١٢.٠٠","10000000000000",".12","1."})
    BadData("money grammar "+value,"line2.wages",JsonValue.Create(value),"E_MONEY");
BadData("number money", "line2.wages",JsonValue.Create(100),"E_VALUE_TYPE");
BadData("null value", "line2.wages",null,"E_INPUT_JSON");
BadData("Boolean noncheckbox", "name",JsonValue.Create(true),"E_VALUE_TYPE");
foreach(var value in new[]{"20260925","2026-W01-1","2026-02-29","2026-09-31","２０２６-01-01","2026-9-01"}) BadData("strict date "+value,"line17.finalWageDate",JsonValue.Create(value),"E_DATE");
Test("leap date",()=>{var d=Data();Values(d)["line17.finalWageDate"]="2024-02-29";No(Validate(d),"E_DATE");});
foreach(string format in new[]{"yyyy-MM-dd","MM/dd/yyyy"})
    BadProfile("date cell layout rejects ten-character format "+format,p=>Key(p,"line17.finalWageDate")["format"]=format,"E_PROFILE_DATE_LAYOUT");
foreach(var value in new[]{"True","FALSE","yes","no","1","0","x"," true","false "}) BadData("checkbox grammar "+value,"line17.businessClosed",JsonValue.Create(value),"E_CHECKBOX");
Test("required checkbox requires true",()=>{var p=Profile();Key(p,"line17.businessClosed")["required"]=true;var d=Data();Values(d)["line17.businessClosed"]=false;Has(Validate(d,p),"E_REQUIRED");});
Test("scalar cap",()=>{var d=Data();Values(d)["name"]=new string('X',257);Has(Validate(d),"E_TEXT_LIMIT");});
Test("scalar count not UTF16 count",()=>{var d=Data();Values(d)["name"]=string.Concat(Enumerable.Repeat("😀",256));No(Validate(d),"E_TEXT_LIMIT");});
Test("required unicode whitespace",()=>{var d=Data();Values(d)["name"]="\u2003\t";Has(Validate(d),"E_REQUIRED");});
Test("optional whitespace preserved",()=>{var d=Data();Values(d)["tradeName"]="  ";var r=Validate(d);Assert(r.Values["tradeName"].Text=="  "&&r.Values["tradeName"].IsPopulated);});
Test("identifier leading zeros",()=>{var d=Data();Values(d)["ein"]="00-0123456";Assert(Validate(d).Values["ein"].Text=="000123456");});
Test("identifier unicode digits rejected",()=>{var d=Data();Values(d)["ein"]="١٢٣٤٥٦٧٨٩";Has(Validate(d),"E_PATTERN");});
Test("blank and missing tracked",()=>{var d=Data();Values(d)["tradeName"]="";var r=Validate(d);Assert(r.Values["tradeName"].Presence==ValuePresence.Blank&&r.Values["designee.pin"].Presence==ValuePresence.Missing);});
Test("negative subdollar rendering",()=>{var parts=ValueNormalizer.FormatMoney(-2,true,"blank");Assert(parts==( "-0","02"));});
Test("negative zero canonical",()=>{Assert(ValueNormalizer.TryParseCents("-0.00",out long cents)&&cents==0);Assert(ValueNormalizer.FormatMoney(cents,true,"show")==("0","00"));});
Test("money equivalent spelling",()=>{var a=new NormalizedValue("money",ValuePresence.Supplied,true,Cents:2730000);foreach(var s in new[]{"27300","27300.0","27300.00"}){Assert(ValueNormalizer.TryParseCents(s,out var c));Assert(ValueNormalizer.Equivalent(a,a with{Cents=c}));}});
BadProfile("empty hash allowlist",p=>p["template"]!["sha256Allowlist"]=new JsonArray(),"E_TEMPLATE_ALLOWLIST");
BadProfile("unknown member",p=>p["typo"]=true);
BadProfile("unknown key member",p=>Key(p,"name")["typo"]=true);
BadProfile("kind-specific member",p=>Key(p,"name")["dollars"]="x","E_PROFILE_KIND_MEMBER");
BadProfile("duplicate key",p=>p["keys"]!.AsArray().Add(p["keys"]![0]!.DeepClone()),"E_PROFILE_KEY");
BadProfile("duplicate target",p=>Key(p,"tradeName")["targets"]=Key(p,"name")["targets"]!.DeepClone(),"E_PROFILE_TARGET_OWNER");
BadProfile("invalid split shape",p=>Key(p,"ein")["parts"]=new JsonArray(2,6,1),"E_PROFILE_SPLIT");
BadProfile("invalid regex",p=>Key(p,"name")["pattern"]="[","E_PROFILE_PATTERN");
BadProfile("unknown severity",p=>p["rules"]![0]!["severity"]="warn","E_PROFILE_RULE_SEVERITY");
BadProfile("invalid rule reference",p=>p["rules"]![0]!["total"]="unknown","E_PROFILE_RULE_OPERANDS");
BadProfile("invalid rule money type",p=>p["rules"]![0]!["total"]="ein","E_PROFILE_RULE_OPERANDS");
BadProfile("duplicate rule id",p=>p["rules"]![1]!["id"]=p["rules"]![0]!["id"]!.DeepClone(),"E_PROFILE_RULE_ID");
BadProfile("string Boolean condition",p=>p["rules"]![6]!["when"]!["equals"]="true","E_PROFILE_CONDITION");
BadProfile("numeric money condition",p=>p["rules"]![0]!["when"]=JsonNode.Parse("{\"key\":\"line2.wages\",\"equals\":100}"),"E_PROFILE_CONDITION");
BadProfile("empty choice assignments",p=>Key(p,"quarter")["options"]!["1"]=new JsonArray(),"E_PROFILE_ONEOF");
BadProfile("choice overlapping targets",p=>Key(p,"quarter")["options"]!["2"]=Key(p,"quarter")["options"]!["1"]!.DeepClone(),"E_PROFILE_TARGET_OWNER");
BadProfile("unknown capability",p=>p["capabilities"]!.AsArray().Add("arbitrary"),"E_PROFILE_CAPABILITY");
BadProfile("missing explicit boolean",p=>Key(p,"name").Remove("required"));
BadProfile("null profile nested member",p=>p["template"]=null);
BadProfile("null array element",p=>p["keys"]!.AsArray().Add((JsonNode?)null));
Test("passing warning preserves prior error",()=>{var p=Profile();p["rules"]![1]!["severity"]="warning";var d=Data();Values(d)["line5a.ssTax"]="12400.01";var r=Validate(d,p);Assert(r.Findings.Any(x=>x.RuleId=="irs-941.rule01"&&x.Severity=="error"));});
Test("four warning findings stay warnings",()=>{var p=Profile();p["rules"]![5]!["severity"]="warning";var d=Data("941-2026q3-monthly-final-refund");foreach(var k in new[]{"line16.month1","line16.month2","line16.month3","line16.total"})Values(d).Remove(k);var r=Validate(d,p);Assert(r.Findings.Count(x=>x.RuleId=="irs-941.rule06"&&x.Severity=="warning")==4);});
Test("malformed operand skips arithmetic",()=>{var d=Data();Values(d)["line5a.ssTax"]="bad";var r=Validate(d);Assert(r.Rules.Single(x=>x.RuleId=="irs-941.rule01").State=="skipped_invalid_operand");Assert(!r.Findings.Any(x=>x.RuleId=="irs-941.rule01"));});
Test("monthly one-cent equality",()=>{var d=Data("941-2026q3-monthly-final-refund");Values(d)["line16.month3"]="2529.99";Values(d)["line16.total"]="7589.99";var r=Validate(d);Has(r,"E_EQUAL");Assert(!r.Findings.Any(x=>x.RuleId=="irs-941.rule05"));});
Test("monthly 3.00 counterexample",()=>{var d=Data("941-2026q3-monthly-final-refund");foreach(var k in new[]{"line16.month1","line16.month2","line16.month3"})Values(d)[k]="1.00";Values(d)["line16.total"]="3.00";Has(Validate(d),"E_EQUAL");});
Test("semiweekly empty monthly skeleton accepted",()=>{var d=Data();foreach(var k in new[]{"line16.month1","line16.month2","line16.month3","line16.total"})Values(d)[k]="";Assert(Validate(d).IsValid);});
Test("inactive explicit zero rejected",()=>{var d=Data();Values(d)["line16.month1"]="0.00";Has(Validate(d),"E_INACTIVE_SECTION");});
Test("printed schedule context mismatch",()=>{var d=Data();Values(d)["line16.depositSchedule"]="monthly";Has(Validate(d),"E_CONTEXT_SCHEDULE");});
Test("under2500 retains explicit context",()=>{var d=Data();Values(d)["line16.depositSchedule"]="under2500";Assert(Validate(d).IsValid);});
foreach(var prefix in new[]{"00","13","20","33","99"}) Test("routing reject prefix "+prefix,()=>{var d=Data("941-2026q3-monthly-final-refund");Values(d)["line15c.routingNumber"]=prefix+"1234567";Has(Validate(d),"E_PATTERN");});
foreach(var prefix in new[]{"01","12","21","32"}) Test("routing accept prefix "+prefix,()=>{var d=Data("941-2026q3-monthly-final-refund");Values(d)["line15c.routingNumber"]=prefix+"1234567";No(Validate(d),"E_PATTERN");});
Test("account hyphen accepted",()=>{var d=Data("941-2026q3-monthly-final-refund");Values(d)["line15e.accountNumber"]="00-ABC-123";No(Validate(d),"E_PATTERN");});
Test("account spaces rejected",()=>{var d=Data("941-2026q3-monthly-final-refund");Values(d)["line15e.accountNumber"]="00 ABC";Has(Validate(d),"E_PATTERN");});
Test("unbound validation never render ready",()=>{var r=Validate(Data());Assert(!r.ReadyToRender&&r.ValidationLevels["binding"]=="not_run"&&r.FilingReadiness=="not_assessed");});
Test("assignment full coverage and explicit clears",()=>{var r=Validate(Data());var a=AssignmentPlanner.Build(r);Assert(a.Count==116);foreach(var k in new[]{"tradeName","line15c.routingNumber","line15e.accountNumber","designee.pin","line17.finalWageDate","line15b.overpaymentChoice"})Assert(a.Where(x=>x.Key==k).All(x=>x.IsClear));Assert(a.Count(x=>x.Key=="quarter"&&x.Checked)==2);});
Test("Schedule B assignment coverage",()=>Assert(AssignmentPlanner.Build(Validate(Data("941sb-2026q3"))).Count==212));
var job = PacketValidator.ParseJob(File.ReadAllBytes(Path.Combine(root,"samples/job-2026q3-semiweekly.json"))).Value!;
Test("packet valid pair",()=>Assert(PacketValidator.Validate(job,new Dictionary<string,ValidationReport>{{"941",Validate(Data())},{"941sb",Validate(Data("941sb-2026q3"))}}).Count==0));
foreach(var key in new[]{"ein","name","quarter","calendarYear"}) Test("packet mismatched "+key,()=>{var b=Data("941sb-2026q3");Values(b)[key]=key switch{"ein"=>"003456789","name"=>"OTHER EMPLOYER","quarter"=>"2",_=>"2025"};var br=Validate(b);var f=PacketValidator.Validate(job,new Dictionary<string,ValidationReport>{{"941",Validate(Data())},{"941sb",br}});Assert(f.Count>0||!br.IsValid);});
Test("nonexistent September31 explicit zero",()=>{var b=Data("941sb-2026q3");Values(b)["month3.day31"]="0.00";Assert(PacketValidator.ValidateCalendar(Validate(b)).Any(x=>x.Code=="E_CALENDAR_DAY"));});
Test("nonexistent day blank accepted",()=>{var b=Data("941sb-2026q3");Values(b)["month3.day31"]="";Assert(PacketValidator.ValidateCalendar(Validate(b)).Count==0);});
foreach(var pair in new[]{("2024",false),("2026",true)}) Test("February29 calendar "+pair.Item1,()=>{var p=Profile("941sb");p["filingYears"]=new JsonArray(pair.Item1);var b=Data("941sb-2026q3");b["context"]!["taxYear"]=pair.Item1;b["context"]!["quarter"]="1";Values(b)["calendarYear"]=pair.Item1;Values(b)["quarter"]="1";Values(b)["month2.day29"]="0.00";Assert(PacketValidator.ValidateCalendar(Validate(b,p)).Any(x=>x.Code=="E_CALENDAR_DAY")==pair.Item2);});
foreach(var path in new[]{"/out.pdf","C:\\out.pdf","../out.pdf","a/../b.pdf","a//b.pdf","NUL.pdf","a./b.pdf","result.json"}) Test("unsafe output "+path,()=>{var j=JsonNode.Parse(File.ReadAllBytes(Path.Combine(root,"samples/job-2026q3-semiweekly.json")))!;j["documents"]![0]!["output"]=path;Assert(!PacketValidator.ParseJob(Bytes(j)).IsValid);});
Test("packet requires flatten",()=>{var j=JsonNode.Parse(File.ReadAllBytes(Path.Combine(root,"samples/job-2026q3-semiweekly.json")))!;j["flatten"]=false;Assert(!PacketValidator.ParseJob(Bytes(j)).IsValid);});
Test("value-free diagnostics",()=>{var d=Data();Values(d)["SENSITIVE_SENTINEL_UNKNOWN"]="SENSITIVE_SENTINEL_VALUE";Values(d)["line2.wages"]="SENSITIVE_SENTINEL_MONEY";var r=Validate(d);var diagnostic=JsonSerializer.Serialize(r.Findings,StrictJson.Options);Assert(!diagnostic.Contains("SENSITIVE_SENTINEL",StringComparison.Ordinal));});
Test("requiredIf gates arithmetic before rule order",()=>{var d=Data("941-2026q3-monthly-final-refund");Values(d).Remove("line16.month3");var r=Validate(d);Assert(r.Rules.Single(x=>x.RuleId=="irs-941.rule05").State=="skipped_required_operand");Assert(!r.Findings.Any(x=>x.RuleId=="irs-941.rule05"));});
Test("explicit false is allOrNone supplied",()=>{var p=Profile();p["rules"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"test.presence\",\"kind\":\"allOrNone\",\"severity\":\"error\",\"keys\":[\"line18.seasonalEmployer\",\"tradeName\"]}"));var d=Data();Values(d)["line18.seasonalEmployer"]=false;Assert(Validate(d,p).Findings.Any(x=>x.RuleId=="test.presence"&&x.Code=="E_ALL_OR_NONE"));});
foreach (var fixture in new[]{("941","941-2026q3-semiweekly"),("941sb","941sb-2026q3")})
    Test("independent all-target manifest sentinels "+fixture.Item1,()=>
    {
        var p=ParseP(Profile(fixture.Item1));
        var d=DataValidator.ParseData(Bytes(Data(fixture.Item2))).Value!;
        var original=JsonNode.Parse(File.ReadAllBytes(Path.Combine(root,"fixtures/contracts/legacy/profiles",p.Id+"-"+p.Revision+".json")))!;
        var supplied=new Dictionary<string,NormalizedValue>(StringComparer.Ordinal);
        var expected=new Dictionary<string,(string Text,bool Checked)>(StringComparer.Ordinal);
        int index=0;
        foreach(var node in original["keys"]!.AsArray())
        {
            var k=node!;index++;string key=k["key"]!.ToString(),kind=k["kind"]!.ToString();
            string sentinel="SENTINEL_"+index.ToString("000");long cents=12000+index*101;
            if(kind=="split")sentinel=index.ToString().PadLeft(k["parts"]!.AsArray().Sum(x=>x!.GetValue<int>()),'0');
            if(kind=="oneOf")sentinel=k["options"]!.AsObject().First().Key;
            supplied[key]=new(kind,ValuePresence.Supplied,true,sentinel,cents,new DateOnly(2026,9,25),true);
            switch(kind)
            {
                case "money": expected[k["dollars"]!.ToString()]=((cents/100).ToString(),false);expected[k["cents"]!.ToString()]=((cents%100).ToString("00"),false);break;
                case "checkbox":expected[k["target"]!.ToString()]=("",true);break;
                case "oneOf":foreach(var option in k["options"]!.AsObject()){var targets=option.Value is JsonArray a?a.Select(t=>t!.ToString()):new[]{option.Value!.ToString()};foreach(var t in targets)expected[t]=("",option.Key==sentinel);}break;
                case "split":foreach(var copy in k["targets"]!.AsArray()){int offset=0,part=0;foreach(var target in copy!.AsArray()){int length=k["parts"]![part++]!.GetValue<int>();expected[target!.ToString()]=(sentinel.Substring(offset,length),false);offset+=length;}}break;
                default:foreach(var target in k["targets"]!.AsArray())expected[target!.ToString()]=(kind=="date"?"09252026":sentinel,false);break;
            }
        }
        // Isolated planner fixture: intentionally bypasses data/rule/fit acceptance, never calls a production fill.
        var manifest=AssignmentPlanner.Build(new ValidationReport(p,d,supplied,Array.Empty<Finding>(),Array.Empty<RuleEvaluation>()));
        Assert(manifest.Count==expected.Count);foreach(var a in manifest)Assert(expected[a.Field]==(a.Text,a.Checked));
    });
foreach(var fixture in new[]{("941","941-2026q3-semiweekly"),("941sb","941sb-2026q3")})
    Test("every checkbox choice manifest "+fixture.Item1,()=>
    {
        var p=ParseP(Profile(fixture.Item1));var baseline=Validate(Data(fixture.Item2));
        foreach(var k in p.Keys.Where(x=>x.Kind=="oneOf"))foreach(var choice in k.Options!)
        {
            var values=baseline.Values.ToDictionary(x=>x.Key,x=>x.Value);values[k.Key]=new("oneOf",ValuePresence.Supplied,true,choice.Key);
            var assignments=AssignmentPlanner.Build(new ValidationReport(p,baseline.Data,values,Array.Empty<Finding>(),Array.Empty<RuleEvaluation>())).Where(x=>x.Key==k.Key).ToArray();
            Assert(assignments.Where(x=>x.Checked).Select(x=>x.Field).Order().SequenceEqual(choice.Value.Select(x=>x.Target).Order()));
            Assert(assignments.Count(x=>x.Checked)+assignments.Count(x=>x.IsClear)==k.EnumerateTargets().Count());
        }
    });
foreach(var candidate in ProductionProfileRegistry.Candidates)
    Test("exact reviewed pair admitted for private development "+candidate.Id,()=>{var bytes=File.ReadAllBytes(Path.Combine(root,"profiles",candidate.Id+"-"+candidate.Revision+".json"));var check=ProductionProfileRegistry.Check(bytes,candidate.TemplateSha256);Assert(check.ExactCandidateMatch&&check.ProductionTrusted&&check.Findings.Count==0);Assert(ProductionProfileRegistry.Bind(bytes,candidate.TemplateSha256).IsValid);Assert(ProductionProfileRegistry.AdoptionScope=="bounded_private_development"&&candidate.ReviewStatus=="accepted_private_development");});
Test("profile snapshot owns admitted bytes",()=>{var bytes=File.ReadAllBytes(Path.Combine(root,"profiles","irs-941-2026-03.json"));var candidate=ProductionProfileRegistry.Candidates.Single(x=>x.Id=="irs-941");var admitted=ProductionProfileRegistry.Bind(bytes,candidate.TemplateSha256).Value!;Array.Fill(bytes,(byte)0);Assert(admitted.ParseDefinition().Id=="irs-941");using var input=admitted.OpenRead();Assert(!input.CanWrite);Assert(input is MemoryStream memory&&!memory.TryGetBuffer(out _));var definition=admitted.ParseDefinition();definition.Keys[0]=definition.Keys[1];Assert(admitted.ParseDefinition().Keys[0].Key!=definition.Keys[0].Key);});
Test("wrong template cannot admit exact profile",()=>{var bytes=File.ReadAllBytes(Path.Combine(root,"profiles","irs-941-2026-03.json"));var other=ProductionProfileRegistry.Candidates.Single(x=>x.Id=="irs-941sb");Assert(!ProductionProfileRegistry.Bind(bytes,other.TemplateSha256).IsValid);});
Test("profile re-save cannot authorize production",()=>{var bytes=Bytes(Profile());var candidate=ProductionProfileRegistry.Candidates.Single(x=>x.Id=="irs-941");Assert(!ProductionProfileRegistry.Check(bytes,candidate.TemplateSha256).ExactCandidateMatch);});
Test("regex timeout is a fixed data error",()=>{var p=Profile();Key(p,"tradeName")["pattern"]="(a+)+";var d=Data();Values(d)["tradeName"]=new string('a',250)+"!";Has(Validate(d,p),"E_PATTERN_TIMEOUT");});
Test("exact sum beyond Int64 does not wrap",()=>{var p=Profile();p["rules"]!.AsArray().Clear();var terms=new JsonArray();for(int i=0;i<10000;i++)terms.Add(JsonNode.Parse("{\"key\":\"line2.wages\",\"sign\":1}"));p["rules"]!.AsArray().Add(new JsonObject{{"id","large.sum"},{"kind","sum"},{"severity","error"},{"total","line12.totalTaxes"},{"terms",terms}});var d=Data();Values(d)["line2.wages"]="9999999999999.99";var r=Validate(d,p);Has(r,"E_SUM");No(r,"E_SUM_RANGE");});
Test("caller-neutral packet policy",()=>{Assert(job.Policy=="irs-941-packet/1");});
Console.WriteLine($"RESULT passed={passed} failed={failed}");
return failed==0?0:1;
