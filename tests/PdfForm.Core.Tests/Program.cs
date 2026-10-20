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

// Configuration-only onboarding unit seam: synthetic identities, no PDF admission or runtime claim.
JsonObject GenericProfile()
{
    return new JsonObject
    {
        ["schema"] = "pdfform.profile/3", ["id"] = "fictional-onboarding", ["revision"] = "2026-10", ["profileVersion"] = 1,
        ["title"] = "Fictional onboarding", ["template"] = new JsonObject
        { ["sha256Allowlist"] = new JsonArray(new string('a',64)), ["title"] = "", ["pageCount"] = 1, ["structureFingerprintV1"] = "sha256:" + new string('b',64) },
        ["capabilities"] = new JsonArray("text.singleWidget", "checkbox.singleWidget"), ["options"] = new JsonObject { ["zeroAmounts"] = "blank" },
        ["keys"] = new JsonArray
        {
            new JsonObject { ["key"] = "name", ["kind"] = "text", ["required"] = true, ["sensitive"] = false, ["maxLength"] = 32, ["targets"] = new JsonArray("Applicant.Name") },
            new JsonObject { ["key"] = "quarter", ["kind"] = "text", ["required"] = false, ["sensitive"] = false, ["targets"] = new JsonArray("Season.Description") },
            new JsonObject { ["key"] = "calendarYear", ["kind"] = "text", ["required"] = false, ["sensitive"] = false, ["targets"] = new JsonArray("Other.Description") },
            new JsonObject { ["key"] = "acknowledged", ["kind"] = "checkbox", ["required"] = false, ["sensitive"] = false, ["target"] = "Acknowledged", ["on"] = "Yes" }
        },
        ["rules"] = new JsonArray(), ["unmapped"] = new JsonArray(), ["extensions"] = new JsonObject()
    };
}
JsonObject GenericData() => new()
{
    ["schema"] = "pdfform.data/3", ["profile"] = "fictional-onboarding@2026-10",
    ["values"] = new JsonObject { ["name"] = "Fictional Example", ["quarter"] = "Spring", ["calendarYear"] = "Not a year", ["acknowledged"] = true }
};
JsonObject GenericCatalog(JsonObject profile)
{
    byte[] bytes = Bytes(profile);
    var texts = new JsonArray(); var checks = new JsonArray();
    foreach (var key in ParseP(profile).Keys)
        foreach (var target in key.EnumerateTargets())
            if (key.Kind is "checkbox" or "oneOf")
                checks.Add(new JsonObject { ["target"] = target, ["page"] = 1, ["rectangle"] = new JsonArray(10,10,20,20), ["onState"] = "Yes", ["mark"] = "cross-10-v1" });
            else texts.Add(new JsonObject
            {
                ["target"] = target, ["page"] = 1, ["rectangle"] = new JsonArray(10,10,210,30),
                ["strategy"] = "preserve-embedded-simple-v1", ["layout"] = "single-line-v1", ["resourceName"] = "FixtureFont", ["baseFont"] = "FixtureFont",
                ["subtype"] = "TrueType", ["encoding"] = "WinAnsiEncoding", ["fontSize"] = 10,
                ["programSha256"] = new string('c',64), ["metricsSha256"] = new string('d',64), ["repertoire"] = "encoded-font-v1"
            });
    return new JsonObject
    {
        ["schema"] = "pdfform.catalog/1", ["template"] = new JsonObject { ["path"] = "template.pdf", ["bytes"] = 1024, ["sha256"] = new string('a',64) },
        ["profile"] = new JsonObject { ["path"] = "profile.json", ["bytes"] = bytes.Length, ["sha256"] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            ["id"] = profile["id"]!.DeepClone(), ["revision"] = profile["revision"]!.DeepClone(), ["profileVersion"] = profile["profileVersion"]!.DeepClone() },
        ["review"] = new JsonObject { ["state"] = "reviewed", ["unresolved"] = new JsonArray() },
        ["policy"] = new JsonObject { ["id"] = FormCatalog.ConfiguredPolicy, ["normalization"] = "none-v1", ["textFields"] = texts, ["checkboxFields"] = checks }
    };
}
ParseResult<FormConfigurationSnapshot> BindGeneric(JsonObject profile, JsonObject catalog) => FormCatalog.Bind(Bytes(catalog), Bytes(profile), new string('a',64), 1024);
void CatalogReject(string name, Action<JsonObject> mutation, string code, int exit = 2) => Test(name, () =>
{
    var profile = GenericProfile(); var catalog = GenericCatalog(profile); mutation(catalog); var result = BindGeneric(profile,catalog);
    Assert(!result.IsValid && result.Value is null && result.Findings.Any(f=>f.Code==code) && FormCatalog.AssessmentExitCode(result.Findings)==exit);
});
Test("v3 context-free data and ordinary tax-like field names", () =>
{
    var profile=ParseP(GenericProfile()); var data=DataValidator.ParseData(Bytes(GenericData())); Assert(data.IsValid&&data.Value!.Context is null);
    var report=DataValidator.Validate(profile,data.Value!); Assert(report.IsValid&&AssignmentPlanner.Build(report).Count==4);
    Assert(!report.Findings.Any(f=>f.Code.StartsWith("E_CONTEXT",StringComparison.Ordinal)));
});
Test("v3 IRS-looking profile identity does not invent filing context",()=>
{
    var p=GenericProfile();p["id"]="irs-941";var d=GenericData();d["profile"]="irs-941@2026-10";
    Assert(DataValidator.Validate(ParseP(p),DataValidator.ParseData(Bytes(d)).Value!).IsValid);
});
Test("v3 forbids data context member",()=>{var d=GenericData();d["context"]=Data()["context"]!.DeepClone();Assert(DataValidator.ParseData(Bytes(d)).Findings.Any(f=>f.Code=="E_INPUT_JSON"));});
Test("v3 forbids profile filingYears member",()=>{var p=GenericProfile();p["filingYears"]=new JsonArray("2026");Assert(ProfileParser.ParseProfile(Bytes(p)).Findings.Any(f=>f.Code=="E_PROFILE_JSON"));});
Test("v2 missing context preserves syntax category",()=>{var d=Data();d.AsObject().Remove("context");Assert(DataValidator.ParseData(Bytes(d)).Findings.Any(f=>f.Code=="E_INPUT_JSON"));});
Test("v2 null context preserves syntax category",()=>{var d=Data();d["context"]=null;Assert(DataValidator.ParseData(Bytes(d)).Findings.Any(f=>f.Code=="E_INPUT_JSON"));});
Test("v2 missing filingYears preserves syntax category",()=>{var p=Profile();p.AsObject().Remove("filingYears");Assert(ProfileParser.ParseProfile(Bytes(p)).Findings.Any(f=>f.Code=="E_PROFILE_JSON"));});
Test("v2 synthetic hints remain unknown",()=>{var p=Profile();p["sampleHints"]=new JsonObject { ["name"]="Fictional" };Assert(ProfileParser.ParseProfile(Bytes(p)).Findings.Any(f=>f.Code=="E_PROFILE_JSON"));});
Test("v3 rejects profile-data version mixing",()=>{var p=ParseP(GenericProfile());var d=DataValidator.ParseData(Bytes(Data())).Value!;Assert(DataValidator.Validate(p,d).Findings.Any(f=>f.Code=="E_DATA_SCHEMA_PAIR"));});
Test("v3 rejects filing-context rule condition",()=>{var p=GenericProfile();p["rules"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"context.rule\",\"kind\":\"requiredIf\",\"severity\":\"error\",\"keys\":[\"name\"],\"when\":{\"context\":\"quarter\",\"equals\":\"1\"}}"));Assert(ProfileParser.ParseProfile(Bytes(p)).Findings.Any(f=>f.Code=="E_PROFILE_CONDITION"));});
Test("v3 key-based rules retain existing normalized semantics",()=>{var p=GenericProfile();p["rules"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"key.rule\",\"kind\":\"requiredIf\",\"severity\":\"error\",\"keys\":[\"quarter\"],\"when\":{\"key\":\"acknowledged\",\"equals\":true}}"));var d=GenericData();d["values"]!.AsObject().Remove("quarter");Assert(DataValidator.Validate(ParseP(p),DataValidator.ParseData(Bytes(d)).Value!).Findings.Any(f=>f.Code=="E_REQUIRED_IF"));});
Test("v3 hints roundtrip without tax envelope",()=>{var p=GenericProfile();p["sampleHints"]=new JsonObject{["name"]="Fictional Sample",["acknowledged"]=true};var parsed=ParseP(p);byte[] emitted=ProfileParser.SerializeProfile(parsed);var raw=JsonNode.Parse(emitted)!;Assert(raw["filingYears"] is null&&raw["sampleHints"]!["name"]!.ToString()=="Fictional Sample");Assert(ProfileParser.ParseProfile(emitted).IsValid&&parsed.GetSampleHint("name")!.Value.GetString()=="Fictional Sample");});
Test("v3 unknown hint cannot add a key",()=>{var p=GenericProfile();p["sampleHints"]=new JsonObject{["unknown"]="Fictional"};Assert(ProfileParser.ParseProfile(Bytes(p)).Findings.Any(f=>f.Code=="E_PROFILE_SAMPLE_HINT"));});
Test("v3 hint type and scalar bounds remain enforced",()=>{var p=GenericProfile();p["sampleHints"]=new JsonObject{["name"]=new string('X',257)};Assert(ProfileParser.ParseProfile(Bytes(p)).Findings.Any(f=>f.Code=="E_PROFILE_SAMPLE_HINT"));});
Test("v3 invalid pattern with hint returns a finding",()=>{var p=GenericProfile();Key(p,"name")["pattern"]="[";p["sampleHints"]=new JsonObject{["name"]="Fictional"};Assert(ProfileParser.ParseProfile(Bytes(p)).Findings.Any(f=>f.Code=="E_PROFILE_PATTERN"));});
Test("v3 duplicate envelope preserves strict duplicate category",()=>{byte[] b=Encoding.UTF8.GetBytes("{\"schema\":\"pdfform.data/3\",\"schema\":\"pdfform.data/3\"}");Assert(DataValidator.ParseData(b).Findings.Any(f=>f.Code=="E_JSON_DUPLICATE"));});
Test("v3 unknown data member stays syntax failure",()=>{var d=GenericData();d["arbitrary"]="PRIVATE_SENTINEL";var r=DataValidator.ParseData(Bytes(d));Assert(r.Findings.Any(f=>f.Code=="E_INPUT_JSON")&&!JsonSerializer.Serialize(r.Findings).Contains("PRIVATE_SENTINEL",StringComparison.Ordinal));});
Test("v3 rejected by IRS packet and calendar validators",()=>{var p=ParseP(GenericProfile());var r=DataValidator.Validate(p,DataValidator.ParseData(Bytes(GenericData())).Value!);Assert(PacketValidator.Validate(job,new Dictionary<string,ValidationReport>{{"941",r}}).Any(f=>f.Code=="E_PACKET_INPUT"));Assert(PacketValidator.ValidateCalendar(r).Any(f=>f.Code=="E_PACKET_CONTEXT"));});
Test("catalog binds exact complete synthetic configuration",()=>{var p=GenericProfile();var r=BindGeneric(p,GenericCatalog(p));Assert(r.IsValid&&!r.Value!.IsBuiltIn&&r.Value.TemplateBytes==1024&&r.Value.ParsePolicy().TextFields.Length==3);});
Test("catalog snapshot owns both buffers and defensive policies",()=>
{
    var p=GenericProfile();byte[] pb=Bytes(p),cb=Bytes(GenericCatalog(p));var r=FormCatalog.Bind(cb,pb,new string('a',64),1024).Value!;Array.Fill(pb,(byte)0);Array.Fill(cb,(byte)0);
    var policy=r.ParsePolicy();policy.TextFields[0].Rectangle[0]=999;Assert(r.ParsePolicy().TextFields[0].Rectangle[0]==10);
    var profile=r.ParseProfile();profile.Keys[0]=profile.Keys[1];Assert(r.ParseProfile().Keys[0].Key=="name");
    using var a=r.OpenCatalogRead();using var b=r.OpenProfileRead();Assert(!a.CanWrite&&!b.CanWrite&&a is MemoryStream am&&!am.TryGetBuffer(out _)&&b is MemoryStream bm&&!bm.TryGetBuffer(out _));
});
CatalogReject("catalog wrong template digest",c=>c["template"]!["sha256"]=new string('e',64),"E_CATALOG_TEMPLATE_HASH");
CatalogReject("catalog wrong profile digest",c=>c["profile"]!["sha256"]=new string('e',64),"E_CATALOG_PROFILE_HASH");
CatalogReject("catalog wrong profile byte count",c=>c["profile"]!["bytes"]=1,"E_CATALOG_PROFILE_HASH");
CatalogReject("catalog wrong profile version",c=>c["profile"]!["profileVersion"]=2,"E_CATALOG_PROFILE_IDENTITY");
CatalogReject("catalog path cannot escape package",c=>c["profile"]!["path"]="../profile.json","E_CATALOG_PROFILE");
CatalogReject("catalog unknown strategy reports missing engine support",c=>c["policy"]!["id"]="plugin-loader","E_CATALOG_POLICY_UNSUPPORTED",3);
CatalogReject("catalog cannot declare arbitrary normalization",c=>c["policy"]!["normalization"]="clear-all-actions","E_CATALOG_POLICY_UNSUPPORTED",3);
CatalogReject("catalog cannot declare radio support",c=>c["policy"]!["checkboxFields"]![0]!["mark"]="radio-group-v1","E_CATALOG_POLICY_UNSUPPORTED",3);
CatalogReject("catalog cannot select unbounded font size",c=>c["policy"]!["textFields"]![0]!["fontSize"]=73,"E_CATALOG_POLICY_UNSUPPORTED",3);
CatalogReject("catalog duplicate target policy",c=>c["policy"]!["textFields"]!.AsArray().Add(c["policy"]!["textFields"]![0]!.DeepClone()),"E_CATALOG_TARGETS");
CatalogReject("catalog missing target policy",c=>c["policy"]!["textFields"]!.AsArray().RemoveAt(0),"E_CATALOG_TARGET_COVERAGE");
CatalogReject("catalog wrong field family",c=>c["policy"]!["textFields"]![0]!["target"]="Acknowledged","E_CATALOG_TARGETS");
CatalogReject("catalog declared export state must agree",c=>c["policy"]!["checkboxFields"]![0]!["onState"]="Other","E_CATALOG_ON_STATE");
CatalogReject("catalog checkbox dimensions cannot choose new layout",c=>c["policy"]!["checkboxFields"]![0]!["rectangle"]=new JsonArray(10,10,30,30),"E_CATALOG_MARK_GEOMETRY");
CatalogReject("catalog draft is configuration-needed not fill-admitted",c=>c["review"]!["state"]="draft","E_CATALOG_REVIEW_REQUIRED",1);
CatalogReject("reviewed catalog cannot retain unresolved issues",c=>c["review"]!["unresolved"]!.AsArray().Add(new JsonObject{["code"]="E_CONFIG_REVIEW_REQUIRED",["capability"]="mapping.review"}),"E_CATALOG_REVIEW");
CatalogReject("catalog cannot substitute built-in authority",c=>{c["policy"]!["id"]=FormCatalog.Form941Policy;c["policy"]!["normalization"]="reviewed-irs-static-hybrid-v1";c["policy"]!["textFields"]=new JsonArray();c["policy"]!["checkboxFields"]=new JsonArray();},"E_CATALOG_BUILTIN_IDENTITY");
Test("catalog required identity members are not defaulted",()=>{var p=GenericProfile();var c=GenericCatalog(p);c["template"]!.AsObject().Remove("bytes");Assert(FormCatalog.ParseCatalog(Bytes(c)).Findings.Any(f=>f.Code=="E_INPUT_JSON"));});
Test("default catalog retains both exact built-in policies",()=>{foreach(var item in ProductionProfileRegistry.Candidates){byte[] bytes=File.ReadAllBytes(Path.Combine(root,"profiles",item.Id=="irs-941"?"irs-941-2026-03.json":"irs-941sb-2024-03.json"));var bound=ProductionProfileRegistry.BindConfiguration(bytes,item.TemplateSha256,item.Id=="irs-941"?841648:97028);Assert(bound.IsValid&&bound.Value!.IsBuiltIn&&bound.Value.ParsePolicy().Id==(item.Id=="irs-941"?FormCatalog.Form941Policy:FormCatalog.ScheduleBPolicy));}});
Test("default catalog does not admit profile reserialization",()=>{var c=ProductionProfileRegistry.Candidates.Single(x=>x.Id=="irs-941");var r=FormCatalog.BindDefault(Bytes(Profile()),c.TemplateSha256,841648);Assert(!r.IsValid&&r.Findings.Any(f=>f.Code=="E_PROFILE_NOT_RELEASED"));});
Test("catalog and profile byte caps remain unchanged",()=>{var r=FormCatalog.Bind(new byte[Limits.MaxJsonBytes+1],[],new string('a',64),1024);Assert(r.Findings.Any(f=>f.Code=="E_JSON_SIZE"));});
Test("compatibility failure classification keeps invalid above engine gaps",()=>{Assert(FormCatalog.AssessmentExitCode([new("E_CATALOG_TEMPLATE_HASH","binding","fixed"),new("E_CATALOG_POLICY_UNSUPPORTED","binding","fixed")])==2);Assert(FormCatalog.AssessmentExitCode([new("E_CATALOG_POLICY_UNSUPPORTED","binding","fixed",Severity:"warning")])==0);});

Test("catalog fixed positive font-size endpoints are declared capabilities",()=>
{
    foreach(double size in new[]{1d,72d}) { var p=GenericProfile();var c=GenericCatalog(p);c["policy"]!["textFields"]![0]!["fontSize"]=size;Assert(BindGeneric(p,c).IsValid); }
});
CatalogReject("catalog auto-size zero is unsupported",c=>c["policy"]!["textFields"]![0]!["fontSize"]=0,"E_CATALOG_POLICY_UNSUPPORTED",3);
CatalogReject("preserved font requires actual source program",c=>c["policy"]!["textFields"]![0]!.AsObject().Remove("programSha256"),"E_CATALOG_POLICY_UNSUPPORTED",3);
Test("bundled font strategy records exact absent source program and encoding",()=>
{
    var p=GenericProfile();var c=GenericCatalog(p);var f=c["policy"]!["textFields"]![0]!;
    f["strategy"]="bundled-liberation-serif-ascii-v1";f["subtype"]="Type1";f["encoding"]="absent";f["repertoire"]="ascii-plus-eacute-v1";f.AsObject().Remove("programSha256");
    Assert(BindGeneric(p,c).IsValid);
});
Test("bundled font strategy retains present source program identity",()=>
{
    var p=GenericProfile();var c=GenericCatalog(p);var f=c["policy"]!["textFields"]![0]!;
    f["strategy"]="bundled-liberation-serif-ascii-v1";f["repertoire"]="ascii-plus-eacute-v1";
    Assert(BindGeneric(p,c).Value!.ParsePolicy().TextFields[0].ProgramSha256==new string('c',64));
});
CatalogReject("catalog cannot choose a replacement output font",c=>c["policy"]!["textFields"]![0]!["outputProgramSha256"]=new string('e',64),"E_INPUT_JSON");
JsonObject GenericDateProfile(string format, bool cells)
{
    var p=GenericProfile();var key=Key(p,"name");key["kind"]="date";key["format"]=format;
    if(cells)
    {
        var rectangles=new JsonArray();for(int i=0;i<8;i++)rectangles.Add(new JsonArray(10+i*20,10,30+i*20,30));
        p["options"]!["dateCellLayouts"]=new JsonArray(new JsonObject{["target"]="Applicant.Name",["templateSha256"]=new string('a',64),["cells"]=rectangles});
    }
    return p;
}
Test("ordinary formatted dates do not imply cell placement",()=>
{
    foreach(string format in new[]{"MMddyyyy","yyyy-MM-dd","MM/dd/yyyy"}) { var p=GenericDateProfile(format,false);Assert(BindGeneric(p,GenericCatalog(p)).IsValid); }
});
Test("explicit eight-cell layout without geometry is configuration-needed",()=>
{
    var p=GenericDateProfile("MMddyyyy",false);var c=GenericCatalog(p);c["policy"]!["textFields"]![0]!["layout"]="eight-date-cells-v1";var r=BindGeneric(p,c);
    Assert(!r.IsValid&&r.Findings.Any(f=>f.Code=="E_CATALOG_DATE_LAYOUT_REQUIRED")&&FormCatalog.AssessmentExitCode(r.Findings)==1);
});
Test("explicit eight-cell layout binds complete geometry",()=>
{
    var p=GenericDateProfile("MMddyyyy",true);var c=GenericCatalog(p);c["policy"]!["textFields"]![0]!["layout"]="eight-date-cells-v1";Assert(BindGeneric(p,c).IsValid);
});
Test("date geometry cannot silently select a single-line policy",()=>
{
    var p=GenericDateProfile("MMddyyyy",true);var r=BindGeneric(p,GenericCatalog(p));Assert(r.Findings.Any(f=>f.Code=="E_CATALOG_DATE_LAYOUT_CONFLICT"));
});
Test("date cell cannot escape exact configured widget",()=>
{
    var p=GenericDateProfile("MMddyyyy",true);p["options"]!["dateCellLayouts"]![0]!["cells"]![7]![2]=211;
    var c=GenericCatalog(p);c["policy"]!["textFields"]![0]!["layout"]="eight-date-cells-v1";Assert(BindGeneric(p,c).Findings.Any(f=>f.Code=="E_CATALOG_DATE_GEOMETRY"));
});
Test("eight-cell layout cannot reinterpret an ordinary text key",()=>
{
    var p=GenericProfile();var c=GenericCatalog(p);c["policy"]!["textFields"]![0]!["layout"]="eight-date-cells-v1";
    Assert(BindGeneric(p,c).Findings.Any(f=>f.Code=="E_CATALOG_POLICY_UNSUPPORTED"));
});

Test("configuration DTO represents an unconfirmed commit without invented identities",()=>
{
    var receipt=new ConfigurationResultDocument
    {
        Operation="save-config",Status="internal_error",ExitCode=4,ArtifactState="unknown",
        ValidationLevels=new Dictionary<string,string>{{"syntax","passed"},{"profile","passed"},{"binding","passed"},{"bindingStructure","passed"},{"data","not_run"},{"packet","not_run"},{"render","not_run"}},
        Outputs=[],Errors=[new("E_PUBLICATION_UNKNOWN","publication","Commit outcome is unconfirmed.")],Warnings=[]
    };
    byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(receipt,StrictJson.Options);var parsed=StrictJson.Parse<ConfigurationResultDocument>(bytes,"configuration-result");
    Assert(parsed.IsValid&&parsed.Value!.ArtifactState=="unknown"&&parsed.Value.Template is null&&parsed.Value.Profile is null&&parsed.Value.Outputs.Length==0);
});
Test("configuration DTO cannot accept foreign run receipt members",()=>
{
    var o=new JsonObject{["schema"]="pdfform.configuration-result/1",["operation"]="configure",["status"]="internal_error",["exitCode"]=4,["artifactState"]="none",["validationLevels"]=new JsonObject(),["outputs"]=new JsonArray(),["errors"]=new JsonArray(),["warnings"]=new JsonArray(),["tool"]=new JsonObject()};
    Assert(StrictJson.Parse<ConfigurationResultDocument>(Bytes(o),"configuration-result").Findings.Any(f=>f.Code=="E_INPUT_JSON"));
});
JsonNode ConfigurationReceiptSchema()=>JsonNode.Parse(File.ReadAllBytes(Path.Combine(root,"schemas","configuration-result.schema.json")))!;
Test("configuration schema fixes all six status exit pairs",()=>
{
    var clauses=ConfigurationReceiptSchema()["allOf"]!.AsArray();
    foreach(var pair in new[]{("ok",0),("validation_failed",1),("configuration_needed",1),("input_error",2),("unsupported",3),("internal_error",4)})
        Assert(clauses.Any(c=>c?["if"]?["properties"]?["status"]?["const"]?.GetValue<string>()==pair.Item1&&c?["then"]?["properties"]?["exitCode"]?["const"]?.GetValue<int>()==pair.Item2));
});
Test("configuration schema distinguishes unknown and confirmed publication",()=>
{
    var clauses=ConfigurationReceiptSchema()["allOf"]!.AsArray();
    var unknown=clauses.Single(c=>c?["if"]?["properties"]?["artifactState"]?["const"]?.GetValue<string>()=="unknown")!;
    Assert(unknown["then"]!["properties"]!["status"]!["const"]!.GetValue<string>()=="internal_error"&&unknown["then"]!["properties"]!["outputs"]!["maxItems"]!.GetValue<int>()==0);
    var published=clauses.Single(c=>c?["if"]?["properties"]?["artifactState"]?["const"]?.GetValue<string>()=="published")!;
    Assert(published["then"]!["required"]!.AsArray().Count==2&&published["then"]!["properties"]!["status"]!["enum"]!.AsArray().Any(x=>x!.GetValue<string>()=="internal_error"));
    var sets=published["then"]!["allOf"]![0]!;Assert(sets["then"]!["properties"]!["outputs"]!["minItems"]!.GetValue<int>()==2&&sets["else"]!["properties"]!["outputs"]!["minItems"]!.GetValue<int>()==3);
});
Test("configuration schema retains truthful unexecuted stages",()=>
{
    var levels=ConfigurationReceiptSchema()["properties"]!["validationLevels"]!;Assert(levels["required"]!.AsArray().Count==7);
    foreach(string stage in new[]{"data","packet","render"})Assert(levels["properties"]![stage]!["const"]!.GetValue<string>()=="not_run");
});

Test("draft missing policies are configuration-needed without weakening invalid bindings",()=>
{
    var p=GenericProfile();var c=GenericCatalog(p);c["review"]!["state"]="draft";c["policy"]!["textFields"]!.AsArray().RemoveAt(0);
    var missing=BindGeneric(p,c);Assert(!missing.IsValid&&missing.Value is null&&FormCatalog.AssessmentExitCode(missing.Findings)==1);
    Assert(missing.Findings.Any(f=>f.Code=="E_CATALOG_TARGET_REQUIRED")&&!missing.Findings.Any(f=>f.Code is "E_CATALOG_TARGET_TYPE" or "E_CATALOG_TARGET_PAGE" or "E_CATALOG_TARGET_COVERAGE"));
    var extra=c.DeepClone().AsObject();var policy=extra["policy"]!["textFields"]![0]!.DeepClone();policy["target"]="Extra.Unowned";extra["policy"]!["textFields"]!.AsArray().Add(policy);Assert(FormCatalog.AssessmentExitCode(BindGeneric(p,extra).Findings)==2);
    var malformed=c.DeepClone().AsObject();malformed["policy"]!["textFields"]![0]!["programSha256"]="invalid";Assert(FormCatalog.AssessmentExitCode(BindGeneric(p,malformed).Findings)==2);
    var family=GenericCatalog(p);family["review"]!["state"]="draft";family["policy"]!["textFields"]![0]!["target"]="Acknowledged";family["policy"]!["checkboxFields"]![0]!["target"]="Applicant.Name";var familyResult=BindGeneric(p,family);Assert(FormCatalog.AssessmentExitCode(familyResult.Findings)==2&&familyResult.Findings.Any(f=>f.Code=="E_CATALOG_TARGET_TYPE"));
    var wrong=c.DeepClone().AsObject();wrong["template"]!["sha256"]=new string('e',64);Assert(FormCatalog.AssessmentExitCode(BindGeneric(p,wrong).Findings)==2);
    c["review"]!["state"]="reviewed";Assert(FormCatalog.AssessmentExitCode(BindGeneric(p,c).Findings)==2);
});

Console.WriteLine($"RESULT passed={passed} failed={failed}");
return failed==0?0:1;
