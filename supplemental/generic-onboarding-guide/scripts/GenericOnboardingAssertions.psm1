#requires -Version 7.4
# PURE ASSERTION SOURCE / NOT_RUN. No I/O, process launch, imports or tree enumeration.
# JsonElements must come from the existing bounded strict parser. The assertion layer
# additionally rejects duplicate/unknown object members at every inspected boundary.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'


# Closed public vocabulary copied from the pinned final CliCompatibilityProjection,
# including its assessment/limit fallbacks. No runtime source loading or code regex fallback.
$script:GoCompatibilityCodes=@'
E_ACTIVE_CONTENT E_ALL_OR_NONE E_APPROVAL_SIGNATURE E_ARGUMENTS E_CATALOG_BUILTIN_IDENTITY E_CATALOG_DATE_GEOMETRY
E_CATALOG_DATE_LAYOUT_CONFLICT E_CATALOG_DATE_LAYOUT_REQUIRED E_CATALOG_FONT E_CATALOG_ISSUES E_CATALOG_MARK_GEOMETRY E_CATALOG_ON_STATE
E_CATALOG_POLICY_UNSUPPORTED E_CATALOG_PROFILE E_CATALOG_PROFILE_HASH E_CATALOG_PROFILE_IDENTITY E_CATALOG_REVIEW E_CATALOG_REVIEW_REQUIRED
E_CATALOG_SCHEMA E_CATALOG_TARGETS E_CATALOG_TARGET_COVERAGE E_CATALOG_TARGET_PAGE E_CATALOG_TARGET_REQUIRED E_CATALOG_TARGET_TYPE
E_CATALOG_TEMPLATE E_CATALOG_TEMPLATE_HASH E_CERTIFICATION E_CHECKBOX E_CHECKBOX_ON_STATE E_CHECK_INTERNAL
E_COMPATIBILITY_ASSESSMENT E_COMPATIBILITY_ISSUE_LIMIT E_CONFIGURATION_CHECKBOX_GEOMETRY E_CONFIGURATION_DESTINATION E_CONFIGURATION_DIAGNOSTIC_LIMIT E_CONFIGURATION_DISCOVERY
E_CONFIGURATION_DRAFT E_CONFIGURATION_FIELD_ID E_CONFIGURATION_FONT E_CONFIGURATION_INPUT E_CONFIGURATION_INTERNAL E_CONFIGURATION_LAYOUT
E_CONFIGURATION_ON_STATE E_CONFIGURATION_OPERATION E_CONFIGURATION_POLICY E_CONFIGURATION_PUBLICATION E_CONFIGURATION_PUBLICATION_UNCONFIRMED E_CONFIGURATION_RESULT_CONTRACT
E_CONFIGURATION_RESULT_OUTPUTS E_CONFIGURATION_REVIEW_REQUIRED E_CONFIGURATION_REVIEW_STATE E_CONFIGURATION_SIZE E_CONFIGURATION_TEMPLATE E_CONFIGURATION_TEMPLATE_MISMATCH
E_CONFIGURATION_TEMPLATE_REQUIRED E_CONFIGURATION_UNRESOLVED E_CONFIG_CHECKBOX_GEOMETRY E_CONFIG_CHECKBOX_TARGET E_CONFIG_DATE_LAYOUT E_CONFIG_FONT_IDENTITY
E_CONFIG_FONT_PROGRAM E_CONFIG_FONT_SIZE E_CONFIG_FONT_STRATEGY E_CONFIG_FONT_STRUCTURE E_CONFIG_TEXT_LAYOUT E_CONFIG_TEXT_TARGET
E_CONFIG_WIDGET_ATTRIBUTES E_CONFIG_WIDGET_BOUNDS E_CONFIG_WIDGET_ROTATION E_CONTEXT E_CONTEXT_QUARTER E_CONTEXT_SCHEDULE
E_CONTEXT_SCHEDULE_B E_CONTEXT_YEAR E_DATA_LIMIT E_DATA_PROFILE E_DATA_SCHEMA E_DATE
E_EMBEDDED_CONTENT E_EQUAL E_EXTENSIONS E_FIELD_ATTRIBUTE E_FIELD_CAPABILITY E_FIELD_CYCLE
E_FIELD_NAME E_FIELD_NO_WIDGET E_FIELD_PARENT E_FIELD_POLICY E_FIELD_TREE E_FIELD_TYPE
E_FILING_YEAR E_FILL_INTERNAL E_FIT_INTERNAL E_FONT_FIXED_SIZE E_FONT_GLYPH E_FONT_GLYPH_UNQUALIFIED
E_FONT_MAPPING E_FONT_NOT_EMBEDDED E_FONT_OUTLINE_GLYPH E_FONT_PROGRAM E_FONT_RESOURCE E_FORCE_UNQUALIFIED
E_INACTIVE_SECTION E_INPUT_FILE E_INPUT_JSON E_JSON_DUPLICATE E_JSON_MEMBERS E_JSON_SIZE
E_JSON_UNICODE E_JSON_UTF8 E_MONEY E_MONEY_NEGATIVE E_NO_ACROFORM E_NO_FIELDS
E_OPTION E_ORPHAN_WIDGET E_PATTERN E_PATTERN_TIMEOUT E_PDF_BOOLEAN E_PDF_ENCRYPTED
E_PDF_INPUT E_PDF_LIMIT E_PDF_PARSE E_PDF_SIZE E_PERMISSION_UNKNOWN E_POSTCOMMIT_REPORTING
E_PROFILE_CAPABILITY E_PROFILE_CHECKBOX E_PROFILE_CONDITION E_PROFILE_DATE E_PROFILE_DATE_LAYOUT E_PROFILE_IDENTITY
E_PROFILE_JSON E_PROFILE_KEY E_PROFILE_KIND E_PROFILE_KIND_MEMBER E_PROFILE_LIMIT E_PROFILE_MONEY
E_PROFILE_NORMALIZATION E_PROFILE_NOT_RELEASED E_PROFILE_ONEOF E_PROFILE_PATTERN E_PROFILE_REVIEW_PENDING E_PROFILE_RULE_ID
E_PROFILE_RULE_KIND E_PROFILE_RULE_OPERANDS E_PROFILE_RULE_SEVERITY E_PROFILE_SCHEMA E_PROFILE_SIZE E_PROFILE_SOURCE
E_PROFILE_SPLIT E_PROFILE_TARGETS E_PROFILE_TARGET_OWNER E_PROFILE_UNMAPPED E_PROFILE_YEARS E_PROFILE_ZERO
E_PUBLICATION E_PUBLICATION_PREFLIGHT E_RELEASE_PROFILE_INVALID E_REQUIRED E_REQUIRED_IF E_REVIEWED_STRUCTURE
E_SAMPLE_BUDGET E_SAMPLE_CONTEXT_REQUIRED E_SAMPLE_CONTEXT_RULE E_SAMPLE_CONTEXT_UNEXPECTED E_SAMPLE_FIT E_SAMPLE_INTERNAL
E_SAMPLE_LIMIT E_SAMPLE_NO_CANDIDATE E_SAMPLE_PROFILE E_SAMPLE_RULE E_SAMPLE_RULE_BUDGET E_SAMPLE_RULE_UNSUPPORTED
E_SAMPLE_UNSATISFIED E_SPLIT_CAPACITY E_SPLIT_LENGTH E_SUM E_TARGET_ALIGNMENT E_TARGET_FLAGS
E_TARGET_FONT E_TARGET_LENGTH E_TARGET_MISSING E_TARGET_OVERLAP E_TARGET_REQUIRED E_TARGET_TYPE
E_TARGET_UNMAPPED E_TARGET_WIDGETS E_TEMPLATE_ALLOWLIST E_TEMPLATE_CONTRACT E_TEMPLATE_HASH E_TEMPLATE_NOT_REVIEWED
E_TEMPLATE_PAGES E_TEMPLATE_PREFILLED E_TEMPLATE_PROFILE E_TEMPLATE_STRUCTURE E_TEMPLATE_TITLE E_TEXT_COMB_CAPACITY
E_TEXT_COMB_OVERFLOW E_TEXT_CONTROL E_TEXT_LAYOUT E_TEXT_LENGTH E_TEXT_LIMIT E_TEXT_MAX_LENGTH
E_TEXT_OVERFLOW E_TEXT_VERTICAL_OVERFLOW E_UNKNOWN_KEY E_UNMAPPED_UNSUPPORTED E_USAGE_RIGHTS_UNAPPROVED E_VALIDATE_INTERNAL
E_VALUE_TYPE E_WIDGET_DUPLICATE E_WIDGET_GEOMETRY E_WIDGET_NOT_ON_PAGE E_WIDGET_PARENT E_XFA_UNSUPPORTED
E_XFA_XML W_ACCEPTANCE_NOT_COMPLETE W_AUTHORING_VALIDATION_ONLY W_CATALOG_JAVASCRIPT_REMOVAL W_CHECKBOX_FONT_PORTABILITY_UNQUALIFIED W_COMPATIBILITY_ASSESSMENT
W_COMPATIBILITY_ISSUE_LIMIT W_CONFIGURATION_CLEANUP_INCOMPLETE W_CONFIGURATION_DIAGNOSTIC_LIMIT W_CONFIGURATION_DRAFT_REVIEW W_FONT_NOT_EMBEDDED W_FONT_UNEMBEDDED_PORTABILITY_UNQUALIFIED
W_NORMAL_OFF_MISSING W_OWNED_CLEANUP_INCOMPLETE W_RENDER_CHECKS_NOT_RUN W_SAMPLE_OPTIONAL_OMITTED
'@ -split '\s+'
$script:GoCompatibilityFeatures=@('field.radio','field.pushbutton','field.choice','field.combo','field.list','field.signature','field.unknown','field.singleWidget','field.readOnly','field.noExport','text.multiline','text.richText','text.password','font.resource','font.fixedSize1To72','widget.rotation','font.simpleType1OrTrueType','font.supportedSourceEncoding','font.validSourceProgram','checkbox.normalExportState','checkbox.fixedMarkGeometry','form.acroform','xfa.configuredAcroform','active.action','signature.configuredAcroform','pdf.encryption','engine.unsupportedCapability')
$script:GoCompatibilityRequirements=@('exactCatalogProfileBinding','renderPolicy','operatorReview')
$script:GoCompatibilityUnassessed=@('businessMeaning','suppliedValueFit','viewerAcceptance','physicalPrint','filingReadiness','staticPageBlankness')
$script:GoCompatibilityCapabilities=@('active.action','checkbox.mark','configuration.assessment','configuration.profile','configuration.review','field.structure','font.source','form.blankOriginal','input.syntax','pdf.encryption','signature.configuredAcroform','template.identity','text.layout','xfa.configuredAcroform')

function Fail-Go([string]$Code) { throw $Code }
function Assert-GoObject([System.Text.Json.JsonElement]$Value,[string[]]$Required,[string[]]$Optional=@()) {
    if ($Value.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { Fail-Go 'E_GO_OBJECT' }
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($property in $Value.EnumerateObject()) {
        if(-not $seen.Add($property.Name) -or ($Required -cnotcontains $property.Name -and $Optional -cnotcontains $property.Name)) { Fail-Go 'E_GO_OBJECT_MEMBERS' }
    }
    foreach($name in $Required) { if(-not $seen.Contains($name)) { Fail-Go 'E_GO_OBJECT_MEMBERS' } }
}
function Get-GoString([System.Text.Json.JsonElement]$Value) {
    if($Value.ValueKind -ne [System.Text.Json.JsonValueKind]::String) { Fail-Go 'E_GO_STRING' }
    $text=$Value.GetString()
    if([string]::IsNullOrWhiteSpace($text) -or $text.Length -gt 4096 -or $text -cmatch '[\x00-\x1f\x7f]') { Fail-Go 'E_GO_STRING' }
    return $text
}
function Get-GoInteger([System.Text.Json.JsonElement]$Value,[long]$Minimum=0,[long]$Maximum=[long]::MaxValue) {
    [long]$number=0
    if($Value.ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or -not $Value.TryGetInt64([ref]$number) -or $number -lt $Minimum -or $number -gt $Maximum) { Fail-Go 'E_GO_INTEGER' }
    return $number
}
function Assert-GoText([System.Text.Json.JsonElement]$Value,[string]$Expected) {
    if((Get-GoString $Value) -cne $Expected) { Fail-Go 'E_GO_TEXT_MISMATCH' }
}
function Get-GoHash([System.Text.Json.JsonElement]$Value) {
    $hash=Get-GoString $Value
    if($hash -cnotmatch '\A[0-9a-f]{64}\z') { Fail-Go 'E_GO_HASH' }
    return $hash
}
function Assert-GoArray([System.Text.Json.JsonElement]$Value,[int]$Minimum=0,[int]$Maximum=1000) {
    if($Value.ValueKind -ne [System.Text.Json.JsonValueKind]::Array -or $Value.GetArrayLength() -lt $Minimum -or $Value.GetArrayLength() -gt $Maximum) { Fail-Go 'E_GO_ARRAY' }
}
function Assert-GoNoErrors([System.Text.Json.JsonElement]$Result) {
    Assert-GoArray ($Result.GetProperty('errors')) 0 0
}
function Assert-GoLevels([System.Text.Json.JsonElement]$Result,[Collections.IDictionary]$Expected) {
    $levels=$Result.GetProperty('validationLevels')
    Assert-GoObject $levels @('syntax','profile','binding','bindingStructure','data','packet','render')
    foreach($name in $Expected.Keys) {
        $state=Get-GoString ($levels.GetProperty($name))
        if(@($Expected[$name]) -cnotcontains $state) { Fail-Go 'E_GO_VALIDATION_LEVEL' }
    }
}
function Assert-GoObservedPin($Pin,$Observed) {
    # Pin is copied from an original approved record or a validated original receipt.
    # Never establish a new expected hash from a post-inspection/render observation.
    if($null -eq $Pin -or $null -eq $Observed -or $Pin.Path -isnot [string] -or $Pin.Sha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
       ($Pin.Bytes -isnot [long] -and $Pin.Bytes -isnot [int]) -or ($Observed.Bytes -isnot [long] -and $Observed.Bytes -isnot [int]) -or $Pin.Bytes -lt 0 -or
       $Pin.NativeIdentity -isnot [string] -or [string]::IsNullOrWhiteSpace($Pin.NativeIdentity) -or
       $Observed.Path -cne $Pin.Path -or $Observed.NativeIdentity -cne $Pin.NativeIdentity -or
       $Observed.Bytes -ne $Pin.Bytes -or $Observed.Sha256 -cne $Pin.Sha256) { Fail-Go 'E_GO_PROTECTED_ARTIFACT_CHANGED' }
}
function Assert-GoProtectedSet([Collections.IDictionary]$OriginalPins,[Collections.IDictionary]$Observed) {
    if($OriginalPins.Count -ne $Observed.Count) { Fail-Go 'E_GO_PROTECTED_SET' }
    foreach($name in $OriginalPins.Keys) {
        if(-not $Observed.Contains($name)) { Fail-Go 'E_GO_PROTECTED_SET' }
        Assert-GoObservedPin $OriginalPins[$name] $Observed[$name]
    }
}
function Assert-GoTemplateObservation([System.Text.Json.JsonElement]$Value,$TemplatePin,[string]$ExpectedLogicalPath) {
    Assert-GoObject $Value @('path','bytes','sha256')
    Assert-GoText ($Value.GetProperty('path')) $ExpectedLogicalPath
    if((Get-GoInteger ($Value.GetProperty('bytes')) 1 4194304) -ne $TemplatePin.Bytes -or
       (Get-GoHash ($Value.GetProperty('sha256'))) -cne $TemplatePin.Sha256) { Fail-Go 'E_GO_TEMPLATE_IDENTITY' }
}
function Assert-GoCompatibility([System.Text.Json.JsonElement]$Result,[int]$ProcessExit,[string]$ExpectedStatus,$TemplatePin) {
    # Core CompatibilityDocument is the public wire contract. Inspector pages,
    # prefilled and fields are private facts and must never be accepted here.
    Assert-GoObject $Result @('schema','status','exitCode','counts','configurationState','issues','missingEngineFeatures','requiredConfiguration','notAssessed') @('templateSha256','templateBytes')
    Assert-GoText ($Result.GetProperty('schema')) 'pdfform.compatibility/1'
    $codes=@{supported=0;configuration_needed=1;invalid=2;unsupported=3;internal_error=4}
    $status=Get-GoString ($Result.GetProperty('status'))
    $exit=Get-GoInteger ($Result.GetProperty('exitCode')) 0 4
    if(-not $codes.ContainsKey($status) -or $codes[$status] -ne $exit -or $ProcessExit -ne $exit -or $status -cne $ExpectedStatus) { Fail-Go 'E_GO_COMPATIBILITY_TERMINAL' }
    $state=Get-GoString ($Result.GetProperty('configurationState'))
    if($state -cnotin @('absent','draft','reviewed','invalid')) { Fail-Go 'E_GO_CONFIGURATION_STATE' }
    $hashValue=[System.Text.Json.JsonElement]::new();$bytesValue=[System.Text.Json.JsonElement]::new()
    $hasHash=$Result.TryGetProperty('templateSha256',[ref]$hashValue)
    $hasBytes=$Result.TryGetProperty('templateBytes',[ref]$bytesValue)
    if($hasHash) { $observedHash=Get-GoHash $hashValue }
    if($hasBytes) { $observedBytes=Get-GoInteger $bytesValue 1 4194304 }
    # Pre-observation error documents may omit identity. The workflow supplies
    # its independently approved pin, so BOTH observed fields remain mandatory
    # for that contextual acceptance and must match the exact original bytes.
    if($null -ne $TemplatePin -and (-not $hasHash -or -not $hasBytes -or $observedHash -cne $TemplatePin.Sha256 -or $observedBytes -ne $TemplatePin.Bytes)) { Fail-Go 'E_GO_TEMPLATE_IDENTITY' }
    $counts=$Result.GetProperty('counts')
    Assert-GoObject $counts @() @('pages','terminalFields','textFields','checkboxFields','otherFields','widgets','requiredFields','combFields')
    foreach($count in $counts.EnumerateObject()) { $null=Get-GoInteger $count.Value 0 }
    foreach($spec in @(@('missingEngineFeatures',$script:GoCompatibilityFeatures),@('requiredConfiguration',$script:GoCompatibilityRequirements),@('notAssessed',$script:GoCompatibilityUnassessed))) {
        $items=$Result.GetProperty($spec[0]);Assert-GoArray $items 0 1000
        foreach($item in $items.EnumerateArray()) {
            if($spec[1] -cnotcontains (Get-GoString $item)) { Fail-Go 'E_GO_COMPATIBILITY_VOCABULARY' }
        }
    }
    if($ExpectedStatus -cin @('supported','configuration_needed')) {
        Assert-GoArray ($Result.GetProperty('missingEngineFeatures')) 0 0
    }
    $issues=$Result.GetProperty('issues');Assert-GoArray $issues 0 1000
    foreach($issue in $issues.EnumerateArray()) {
        Assert-GoObject $issue @('code','capability') @('fieldId')
        $code=Get-GoString ($issue.GetProperty('code'))
        $capability=Get-GoString ($issue.GetProperty('capability'))
        if($script:GoCompatibilityCodes -cnotcontains $code -or $script:GoCompatibilityCapabilities -cnotcontains $capability) { Fail-Go 'E_GO_COMPATIBILITY_VOCABULARY' }
        foreach($property in $issue.EnumerateObject()) {
            if($property.Name -ceq 'fieldId' -and (Get-GoString $property.Value) -cnotmatch '\Afield_[0-9a-f]{64}\z') { Fail-Go 'E_GO_COMPATIBILITY_FIELD_ID' }
        }
        if($ExpectedStatus -cin @('supported','configuration_needed') -and $code -ceq 'E_TEMPLATE_PREFILLED') { Fail-Go 'E_GO_NOT_BLANK' }
    }
    Assert-GoArray ($Result.GetProperty('requiredConfiguration')) 0 1000
    Assert-GoArray ($Result.GetProperty('notAssessed')) 0 1000
}
function Assert-GoPayloadDescriptors([System.Text.Json.JsonElement]$Result,[Collections.IDictionary]$Expected,[Collections.IDictionary]$ActualPayloads) {
    $outputs=$Result.GetProperty('outputs')
    Assert-GoArray $outputs $Expected.Count $Expected.Count
    if($ActualPayloads.Count -ne $Expected.Count) { Fail-Go 'E_GO_PAYLOAD_SET' }
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($output in $outputs.EnumerateArray()) {
        Assert-GoObject $output @('id','path','bytes','sha256')
        $id=Get-GoString ($output.GetProperty('id'))
        if(-not $Expected.Contains($id) -or -not $ActualPayloads.Contains($id) -or -not $seen.Add($id)) { Fail-Go 'E_GO_PAYLOAD_SET' }
        $path=Get-GoString ($output.GetProperty('path'))
        $maximum=if($id -cin @('template','document')){4194304L}else{1048576L}
        $bytes=Get-GoInteger ($output.GetProperty('bytes')) 1 $maximum
        $sha=Get-GoHash ($output.GetProperty('sha256'))
        $actual=$ActualPayloads[$id]
        if($path -cne $Expected[$id] -or $actual.LogicalPath -cne $path -or $actual.Bytes -ne $bytes -or $actual.Sha256 -cne $sha) { Fail-Go 'E_GO_PAYLOAD_IDENTITY' }
    }
}
function Assert-GoConfigurationWarnings([System.Text.Json.JsonElement]$Result) {
    $warnings=$Result.GetProperty('warnings');Assert-GoArray $warnings 0 1000
    foreach($warning in $warnings.EnumerateArray()) {
        Assert-GoObject $warning @('code','stage','message','severity') @('key','field','ruleId')
        $code=Get-GoString ($warning.GetProperty('code'))
        $message=Get-GoString ($warning.GetProperty('message'))
        if($code -cnotmatch '\AW_[A-Za-z0-9_.-]{1,126}\z' -or ($message -cne $code -and $message -cne 'Configuration assessment warning.')) { Fail-Go 'E_GO_CONFIGURATION_WARNING' }
        Assert-GoText ($warning.GetProperty('severity')) 'warning'
        if((Get-GoString ($warning.GetProperty('stage'))) -cnotin @('syntax','profile','binding','bindingStructure','data','packet','render','internal')) { Fail-Go 'E_GO_CONFIGURATION_WARNING' }
        foreach($property in $warning.EnumerateObject()) {
            if($property.Name -cin @('key','field','ruleId') -and (Get-GoString $property.Value) -cnotmatch '\Afield_[0-9a-f]{64}\z') { Fail-Go 'E_GO_CONFIGURATION_WARNING' }
        }
    }
}
function Assert-GoConfigurationReceipt([System.Text.Json.JsonElement]$Result,[int]$ProcessExit,[string]$Operation,$TemplatePin,[string]$ObservedTemplateLogicalPath,[Collections.IDictionary]$ActualPayloads,[string[]]$ActualDirectoryMembers,[bool]$ExistingCompletionValidatorAccepted) {
    # Applies independently to stdout and committed result.json. Terminal stdout must
    # pass too: a committed ok receipt never excuses exit 4/published reporting failure.
    if($Operation -cnotin @('configure','save-config') -or $ProcessExit -ne 0 -or -not $ExistingCompletionValidatorAccepted) { Fail-Go 'E_GO_CONFIGURATION_TERMINAL' }
    Assert-GoObject $Result @('schema','operation','status','exitCode','artifactState','validationLevels','template','profile','outputs','errors','warnings','filingReadiness')
    Assert-GoText ($Result.GetProperty('schema')) 'pdfform.configuration-result/1'
    Assert-GoText ($Result.GetProperty('operation')) $Operation
    Assert-GoText ($Result.GetProperty('status')) 'ok'
    if((Get-GoInteger ($Result.GetProperty('exitCode')) 0 4) -ne 0) { Fail-Go 'E_GO_CONFIGURATION_TERMINAL' }
    Assert-GoText ($Result.GetProperty('artifactState')) 'published'
    Assert-GoText ($Result.GetProperty('filingReadiness')) 'not_assessed'
    Assert-GoNoErrors $Result
    Assert-GoConfigurationWarnings $Result
    Assert-GoLevels $Result ([ordered]@{syntax='passed';profile='passed';binding=@('passed','not_run');bindingStructure=@('passed','not_run');data='not_run';packet='not_run';render='not_run'})
    $expected=[ordered]@{catalog='catalog.json';profile='profile.json'}
    if($Operation -ceq 'save-config') { $expected.Add('template','template.pdf') }
    Assert-GoPayloadDescriptors $Result $expected $ActualPayloads
    $expectedMembers=@($expected.Values)+@('result.json','completion.json')
    $members=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($name in $ActualDirectoryMembers) { if(-not $members.Add($name) -or $expectedMembers -cnotcontains $name) { Fail-Go 'E_GO_CONFIGURATION_MEMBERS' } }
    if($members.Count -ne $expectedMembers.Count) { Fail-Go 'E_GO_CONFIGURATION_MEMBERS' }
    $templatePath=if($Operation -ceq 'save-config'){'template.pdf'}else{$ObservedTemplateLogicalPath}
    Assert-GoTemplateObservation ($Result.GetProperty('template')) $TemplatePin $templatePath
    $profile=$Result.GetProperty('profile')
    Assert-GoObject $profile @('path','bytes','sha256','id','revision','profileVersion')
    Assert-GoText ($profile.GetProperty('path')) 'profile.json'
    $actual=$ActualPayloads['profile']
    if((Get-GoInteger ($profile.GetProperty('bytes')) 1 1048576) -ne $actual.Bytes -or
       (Get-GoHash ($profile.GetProperty('sha256'))) -cne $actual.Sha256) { Fail-Go 'E_GO_PROFILE_IDENTITY' }
    # Json is the bounded-reopened profile bytes, never an identity copied from the receipt.
    $parsed=$actual.Json
    Assert-GoText ($parsed.GetProperty('schema')) 'pdfform.profile/3'
    foreach($property in $parsed.EnumerateObject()) { if($property.Name -ceq 'filingYears') { Fail-Go 'E_GO_TAX_CONTEXT' } }
    foreach($name in @('id','revision')) {
        if((Get-GoString ($profile.GetProperty($name))) -cne (Get-GoString ($parsed.GetProperty($name)))) { Fail-Go 'E_GO_PROFILE_IDENTITY' }
    }
    if((Get-GoInteger ($profile.GetProperty('profileVersion')) 1 2147483647) -ne (Get-GoInteger ($parsed.GetProperty('profileVersion')) 1 2147483647)) { Fail-Go 'E_GO_PROFILE_IDENTITY' }
}
function Assert-GoCommandSuccess([System.Text.Json.JsonElement]$Result,[int]$ProcessExit,[string]$Operation,[string]$ArtifactState) {
    # Final catalog-command envelope, not the broader optional run DTO envelope.
    Assert-GoObject $Result @('schema','operation','status','exitCode','artifactState','validationLevels','tool','profiles','templates','outputs','errors','warnings','stats','filingReadiness')
    Assert-GoText ($Result.GetProperty('schema')) 'pdfform.result/2'
    Assert-GoText ($Result.GetProperty('operation')) $Operation
    Assert-GoText ($Result.GetProperty('status')) 'ok'
    Assert-GoText ($Result.GetProperty('artifactState')) $ArtifactState
    Assert-GoText ($Result.GetProperty('filingReadiness')) 'not_assessed'
    if($ProcessExit -ne 0 -or (Get-GoInteger ($Result.GetProperty('exitCode')) 0 4) -ne 0) { Fail-Go 'E_GO_COMMAND_TERMINAL' }
    Assert-GoNoErrors $Result
    $tool=$Result.GetProperty('tool')
    Assert-GoObject $tool @('name','version','build','engine','runtime','sourceReference')
    foreach($property in $tool.EnumerateObject()) { $null=Get-GoString $property.Value }
    # CatalogCommands emits no profile identities or statistics, including fill.
    Assert-GoArray ($Result.GetProperty('profiles')) 0 0
    Assert-GoObject ($Result.GetProperty('stats')) @()
    $warnings=$Result.GetProperty('warnings');Assert-GoArray $warnings 0 1000
    foreach($warning in $warnings.EnumerateArray()) {
        Assert-GoObject $warning @('code','stage','message','severity') @('key','field','ruleId')
        if((Get-GoString ($warning.GetProperty('code'))) -cnotmatch '\AW_[A-Z0-9_]{1,126}\z') { Fail-Go 'E_GO_COMMAND_WARNING' }
        Assert-GoText ($warning.GetProperty('severity')) 'warning'
        Assert-GoText ($warning.GetProperty('message')) 'Review the indicated compatibility limitation.'
        if((Get-GoString ($warning.GetProperty('stage'))) -cnotin @('syntax','profile','data','binding','packet','render','publication','internal')) { Fail-Go 'E_GO_COMMAND_WARNING' }
        foreach($property in $warning.EnumerateObject()) {
            if($property.Name -cin @('key','field','ruleId') -and (Get-GoString $property.Value) -cnotmatch '\Afield_[0-9a-f]{64}\z') { Fail-Go 'E_GO_COMMAND_WARNING' }
        }
    }
}
function Assert-GoCommandTemplate([System.Text.Json.JsonElement]$Result,$TemplatePin) {
    $templates=$Result.GetProperty('templates');Assert-GoArray $templates 1 1
    $template=$templates[0];Assert-GoObject $template @('id','sha256')
    Assert-GoText ($template.GetProperty('id')) 'template'
    if((Get-GoHash ($template.GetProperty('sha256'))) -cne $TemplatePin.Sha256) { Fail-Go 'E_GO_TEMPLATE_IDENTITY' }
}
function Assert-GoSampleReceipt([System.Text.Json.JsonElement]$Result,[int]$ProcessExit,$TemplatePin,[Collections.IDictionary]$ActualPayloads,[string]$SavedProfileIdentity) {
    # Separate command result: configuration-result/1 permits configure/save-config only.
    Assert-GoCommandSuccess $Result $ProcessExit 'sample-input' 'published'
    Assert-GoCommandTemplate $Result $TemplatePin
    Assert-GoLevels $Result ([ordered]@{syntax='passed';profile='passed';binding='passed';bindingStructure='passed';data='passed';packet='not_run';render='not_run'})
    Assert-GoPayloadDescriptors $Result ([ordered]@{data='data.json'}) $ActualPayloads
    $data=$ActualPayloads['data'].Json
    # This generated sample has exactly these members (CliSyntheticSample.Encode),
    # even though the wider input schema also permits caller extensions.
    Assert-GoObject $data @('schema','profile','values')
    Assert-GoText ($data.GetProperty('schema')) 'pdfform.data/3'
    if($SavedProfileIdentity -cnotmatch '\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}@[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z' -or
       (Get-GoString ($data.GetProperty('profile'))) -cne $SavedProfileIdentity) { Fail-Go 'E_GO_SAMPLE_PROFILE_IDENTITY' }
    $values=$data.GetProperty('values')
    if($values.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { Fail-Go 'E_GO_SAMPLE_VALUES' }
    $keys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($property in $values.EnumerateObject()) {
        if(-not $keys.Add($property.Name) -or $keys.Count -gt 10000) { Fail-Go 'E_GO_SAMPLE_VALUES' }
        if($property.Value.ValueKind -eq [System.Text.Json.JsonValueKind]::String) {
            $value=$property.Value.GetString();$scalars=0
            if($value.Length -gt 512) { Fail-Go 'E_GO_SAMPLE_VALUE_LENGTH' }
            for($i=0;$i -lt $value.Length;$i++) {
                if([char]::IsHighSurrogate($value[$i])) {
                    if($i+1 -ge $value.Length -or -not [char]::IsLowSurrogate($value[$i+1])) { Fail-Go 'E_GO_SAMPLE_VALUE_UNICODE' }
                    $i++
                } elseif([char]::IsLowSurrogate($value[$i])) { Fail-Go 'E_GO_SAMPLE_VALUE_UNICODE' }
                if(++$scalars -gt 256) { Fail-Go 'E_GO_SAMPLE_VALUE_LENGTH' }
            }
        } elseif($property.Value.ValueKind -notin @([System.Text.Json.JsonValueKind]::True,[System.Text.Json.JsonValueKind]::False)) { Fail-Go 'E_GO_SAMPLE_VALUES' }
    }
}
function Assert-GoValidateReceipt([System.Text.Json.JsonElement]$Result,[int]$ProcessExit,$TemplatePin) {
    Assert-GoCommandSuccess $Result $ProcessExit 'validate' 'none'
    Assert-GoCommandTemplate $Result $TemplatePin
    Assert-GoArray ($Result.GetProperty('outputs')) 0 0
    Assert-GoLevels $Result ([ordered]@{syntax='passed';profile='passed';binding='passed';bindingStructure='passed';data='passed';packet='not_run';render='not_run'})
}
function Assert-GoPublishedPdfReceipt([System.Text.Json.JsonElement]$Result,[int]$ProcessExit,$TemplatePin,[Collections.IDictionary]$ActualPayloads) {
    Assert-GoCommandSuccess $Result $ProcessExit 'fill' 'published'
    Assert-GoCommandTemplate $Result $TemplatePin
    Assert-GoLevels $Result ([ordered]@{syntax='passed';profile='passed';data='passed';binding='passed';bindingStructure='passed';render='passed';packet='not_run'})
    Assert-GoPayloadDescriptors $Result ([ordered]@{document='document.pdf'}) $ActualPayloads
}
function Assert-GoDiagnosticReport([byte[]]$RawBytes,[bool]$Json,[string[]]$ForbiddenTokens,[Diagnostics.Stopwatch]$Clock=$null) {
    # Diagnostic channels only. Never call on intentional data/profile/catalog
    # exports. Bounded raw and one-level JSON-decoded text checks; no I/O.
    if($RawBytes.Length -gt 1048576 -or ($Json -and $RawBytes.Length -gt 262144) -or $ForbiddenTokens.Count -gt 512) { Fail-Go 'E_GO_DIAGNOSTIC_CAP' }
    [long]$tokenChars=0
    foreach($token in $ForbiddenTokens) {
        if($token -isnot [string] -or $token.Length -lt 4 -or $token.Length -gt 4096) { Fail-Go 'E_GO_DIAGNOSTIC_TOKEN' }
        $tokenChars+=$token.Length
    }
    if($tokenChars -gt 65536) { Fail-Go 'E_GO_DIAGNOSTIC_CAP' }
    $check={param([string]$Text)
        if($null -ne $Clock -and (-not $Clock.IsRunning -or $Clock.ElapsedMilliseconds -ge 600000)) { Fail-Go 'E_GO_DIAGNOSTIC_DEADLINE' }
        # Includes caller paths that were not supplied as explicit tokens. JSON
        # escaped paths are also recognized after string decoding below.
        if($Text -match '(?i)(?<![A-Za-z0-9])[A-Z]:[\\/]|\\\\[A-Za-z0-9_.-]+[\\/]') { Fail-Go 'E_GO_DIAGNOSTIC_PATH' }
        foreach($token in $ForbiddenTokens) {
            if($null -ne $Clock -and $Clock.ElapsedMilliseconds -ge 600000) { Fail-Go 'E_GO_DIAGNOSTIC_DEADLINE' }
            if($Text.IndexOf($token,[StringComparison]::OrdinalIgnoreCase) -ge 0) { Fail-Go 'E_GO_DIAGNOSTIC_VALUE' }
        }
    }
    $text=[Text.UTF8Encoding]::new($false,$true).GetString($RawBytes)
    & $check $text
    if(-not $Json -or $RawBytes.Length -eq 0) { return }
    $options=[System.Text.Json.JsonDocumentOptions]::new();$options.MaxDepth=24
    $document=[System.Text.Json.JsonDocument]::Parse($text,$options)
    try {
        $pending=[Collections.Generic.Stack[System.Text.Json.JsonElement]]::new();$pending.Push($document.RootElement)
        [long]$chars=0;$nodes=0
        while($pending.Count -gt 0) {
            if(++$nodes -gt 16384) { Fail-Go 'E_GO_DIAGNOSTIC_CAP' }
            $node=$pending.Pop()
            if($null -ne $Clock -and (-not $Clock.IsRunning -or $Clock.ElapsedMilliseconds -ge 600000)) { Fail-Go 'E_GO_DIAGNOSTIC_DEADLINE' }
            if($node.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
                foreach($property in $node.EnumerateObject()) {
                    $chars+=$property.Name.Length;& $check $property.Name;$pending.Push($property.Value)
                    if($pending.Count -gt 16384 -or $chars -gt 1048576) { Fail-Go 'E_GO_DIAGNOSTIC_CAP' }
                }
            } elseif($node.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
                foreach($child in $node.EnumerateArray()) { $pending.Push($child);if($pending.Count -gt 16384) { Fail-Go 'E_GO_DIAGNOSTIC_CAP' } }
            } elseif($node.ValueKind -eq [System.Text.Json.JsonValueKind]::String) {
                $value=$node.GetString();$chars+=$value.Length
                if($chars -gt 1048576) { Fail-Go 'E_GO_DIAGNOSTIC_CAP' }
                & $check $value
            }
        }
    } finally { $document.Dispose() }
}
function Assert-GoReviewedDraftProvenance([System.Text.Json.JsonElement]$Proof,$TemplatePin,[string]$CliSha256,[Collections.IDictionary]$CurrentDraft,[Collections.IDictionary]$ReviewedSources) {
    # One independently pinned root review record, not an approval service. It
    # binds edited sources to an ACTUAL previously generated draft cut. Edited
    # bytes may legitimately differ from sourceDraft bytes.
    Assert-GoObject $Proof @('schema','producerCliSha256','template','sourceDraft','reviewedCopy','reviewReference')
    Assert-GoText ($Proof.GetProperty('schema')) 'pdfform.reviewed-draft-provenance/1'
    if((Get-GoHash ($Proof.GetProperty('producerCliSha256'))) -cne $CliSha256) { Fail-Go 'E_GO_DRAFT_PROVENANCE' }
    Assert-GoTemplateObservation ($Proof.GetProperty('template')) $TemplatePin 'template.pdf'
    $null=Get-GoString ($Proof.GetProperty('reviewReference'))
    foreach($kind in @('sourceDraft','reviewedCopy')) {
        $set=$Proof.GetProperty($kind);Assert-GoObject $set @('catalog','profile')
        $actual=if($kind -ceq 'sourceDraft'){$CurrentDraft}else{$ReviewedSources}
        if($actual.Count -ne 2) { Fail-Go 'E_GO_DRAFT_PROVENANCE' }
        foreach($id in @('catalog','profile')) {
            if(-not $actual.Contains($id)) { Fail-Go 'E_GO_DRAFT_PROVENANCE' }
            $descriptor=$set.GetProperty($id);Assert-GoObject $descriptor @('path','bytes','sha256')
            Assert-GoText ($descriptor.GetProperty('path')) ($id+'.json')
            if((Get-GoInteger ($descriptor.GetProperty('bytes')) 1 1048576) -ne $actual[$id].Bytes -or
               (Get-GoHash ($descriptor.GetProperty('sha256'))) -cne $actual[$id].Sha256) { Fail-Go 'E_GO_DRAFT_PROVENANCE' }
        }
    }
}
function Assert-GoFrozenClosure([System.Text.Json.JsonElement]$Freeze,[System.Text.Json.JsonElement]$Observed,[long]$FirstFixtureSequence) {
    # Exact task11 EngineFreeze/FileImage shapes; evidence itself must already be
    # independently pinned and verified by the admitted host before introduction.
    foreach($root in @($Freeze,$Observed)) {
        Assert-GoObject $root @('sequence','files','launcherSha256')
        $null=Get-GoInteger ($root.GetProperty('sequence')) 1
        $null=Get-GoHash ($root.GetProperty('launcherSha256'))
        Assert-GoArray ($root.GetProperty('files')) 1 1024
    }
    if($FirstFixtureSequence -le (Get-GoInteger ($Freeze.GetProperty('sequence')) 1)) { Fail-Go 'E_GO_FREEZE_INTRODUCTION_ORDER' }
    if((Get-GoInteger ($Freeze.GetProperty('sequence')) 1) -ne (Get-GoInteger ($Observed.GetProperty('sequence')) 1)) { Fail-Go 'E_GO_FROZEN_CLOSURE_CHANGED' }
    if((Get-GoHash ($Freeze.GetProperty('launcherSha256'))) -cne (Get-GoHash ($Observed.GetProperty('launcherSha256')))) { Fail-Go 'E_GO_FROZEN_CLOSURE_CHANGED' }
    $before=$Freeze.GetProperty('files');$after=$Observed.GetProperty('files')
    if($before.GetArrayLength() -ne $after.GetArrayLength()) { Fail-Go 'E_GO_FROZEN_CLOSURE_CHANGED' }
    $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [long]$fileBytes=0;$fileCount=0
    for($i=0;$i -lt $before.GetArrayLength();$i++) {
        foreach($image in @($before[$i],$after[$i])) { Assert-GoObject $image @('relativePath','directory','nativeIdentity','bytes','sha256') }
        $a=$before[$i];$b=$after[$i]
        $name=Get-GoString ($a.GetProperty('relativePath'))
        if(-not $names.Add($name) -or $name -cne (Get-GoString ($b.GetProperty('relativePath'))) -or
           (Get-GoString ($a.GetProperty('nativeIdentity'))) -cne (Get-GoString ($b.GetProperty('nativeIdentity'))) -or
           (Get-GoInteger ($a.GetProperty('bytes'))) -ne (Get-GoInteger ($b.GetProperty('bytes'))) -or
           $a.GetProperty('directory').ValueKind -ne $b.GetProperty('directory').ValueKind -or
           $a.GetProperty('directory').ValueKind -notin @([System.Text.Json.JsonValueKind]::True,[System.Text.Json.JsonValueKind]::False)) { Fail-Go 'E_GO_FROZEN_CLOSURE_CHANGED' }
        if($a.GetProperty('directory').ValueKind -eq [System.Text.Json.JsonValueKind]::False) {
            $fileCount++;$fileBytes+=(Get-GoInteger ($a.GetProperty('bytes')) 0 1073741824)
            if($fileBytes -gt 1073741824) { Fail-Go 'E_GO_FROZEN_CLOSURE_BOUND' }
            if((Get-GoHash ($a.GetProperty('sha256'))) -cne (Get-GoHash ($b.GetProperty('sha256')))) { Fail-Go 'E_GO_FROZEN_CLOSURE_CHANGED' }
        } elseif($a.GetProperty('sha256').ValueKind -ne [System.Text.Json.JsonValueKind]::Null -or $b.GetProperty('sha256').ValueKind -ne [System.Text.Json.JsonValueKind]::Null) { Fail-Go 'E_GO_FROZEN_CLOSURE_CHANGED' }
    }
    if($fileCount -eq 0) { Fail-Go 'E_GO_EMPTY_FROZEN_CLOSURE' }
}
function Assert-GoDisjointRoots([Collections.IDictionary]$Roots) {
    if($Roots.Count -lt 4) { Fail-Go 'E_GO_ROOT_SET' }
    foreach($name in @('bundle','fixtures','configuration','evidence')) { if(-not $Roots.Contains($name)) { Fail-Go 'E_GO_ROOT_SET' } }
    $values=@($Roots.Values)
    for($i=0;$i -lt $values.Count;$i++) {
        $a=$values[$i]
        if($a.Path -isnot [string] -or -not [IO.Path]::IsPathFullyQualified($a.Path) -or [string]::IsNullOrWhiteSpace($a.NativeIdentity)) { Fail-Go 'E_GO_ROOT_IDENTITY' }
        $left=[IO.Path]::GetFullPath($a.Path).TrimEnd('\')
        for($j=$i+1;$j -lt $values.Count;$j++) {
            $b=$values[$j];$right=[IO.Path]::GetFullPath($b.Path).TrimEnd('\')
            if($a.NativeIdentity -ceq $b.NativeIdentity -or [string]::Equals($left,$right,[StringComparison]::OrdinalIgnoreCase) -or
               $left.StartsWith($right+'\',[StringComparison]::OrdinalIgnoreCase) -or $right.StartsWith($left+'\',[StringComparison]::OrdinalIgnoreCase)) { Fail-Go 'E_GO_ROOTS_OVERLAP' }
        }
    }
}
Export-ModuleMember -Function Assert-GoCompatibility,Assert-GoConfigurationReceipt,Assert-GoSampleReceipt,Assert-GoValidateReceipt,Assert-GoPublishedPdfReceipt,Assert-GoDiagnosticReport,Assert-GoReviewedDraftProvenance,Assert-GoObservedPin,Assert-GoProtectedSet,Assert-GoFrozenClosure,Assert-GoDisjointRoots
