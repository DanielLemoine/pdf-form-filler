# SOURCE ONLY / NOT_RUN. Pure controls for the two R4 contract corrections.
# The already admitted owner loads the exact assertion/R3 helpers and modules,
# then explicitly invokes this registrar. No load-time import or execution.
function New-GoR4SampleData {
    return [ordered]@{schema='pdfform.data/3';profile='fictional-workshop@1';values=[ordered]@{participant='FICTIONAL MAPLE MAKER';attending=$true}}
}
function Invoke-GoR4SampleData($Data) {
    $f=New-GoCommandFixture 'sample-input'
    $f.Payloads['data'].Json=Convert-GoCaseElement $Data
    Assert-GoSampleReceipt (Convert-GoCaseElement $f.Receipt) 0 $f.Template $f.Payloads $f.SavedProfileIdentity
}
function Register-GenericOnboardingR4ReviewCases([scriptblock]$Register,$WorkflowModule,$AssertionsModule) {
    # Descriptor byte/hash/native identity reconciliation remains the workflow's
    # independently pinned I/O responsibility. These are decoded-envelope controls.
    & $Register 'r4-sample-string-and-boolean-positive' { Invoke-GoR4SampleData (New-GoR4SampleData) }
    $sampleMutations=@(
        @('extra-root',{param($d)$d.Add('extra','value')},'E_GO_OBJECT_MEMBERS'),
        @('tax-context',{param($d)$d.Add('context',[ordered]@{taxYear='2031'})},'E_GO_OBJECT_MEMBERS'),
        @('wrong-saved-profile',{param($d)$d.profile='different-profile@1'},'E_GO_SAMPLE_PROFILE_IDENTITY'),
        @('values-array',{param($d)$d.values=@()},'E_GO_SAMPLE_VALUES'),
        @('values-null',{param($d)$d.values=$null},'E_GO_SAMPLE_VALUES'),
        @('values-number',{param($d)$d.values=3},'E_GO_SAMPLE_VALUES'),
        @('entry-number',{param($d)$d.values.participant=3},'E_GO_SAMPLE_VALUES'),
        @('entry-null',{param($d)$d.values.participant=$null},'E_GO_SAMPLE_VALUES'),
        @('entry-array',{param($d)$d.values.participant=@('value')},'E_GO_SAMPLE_VALUES'),
        @('entry-object',{param($d)$d.values.participant=[ordered]@{nested='value'}},'E_GO_SAMPLE_VALUES')
    )
    foreach($case in $sampleMutations) {
        $mutate=$case[1];$code=$case[2]
        $body={$d=New-GoR4SampleData;& $mutate $d;Require-GoCaseRejected {Invoke-GoR4SampleData $d} $code}.GetNewClosure()
        & $Register ('r4-sample-'+$case[0]) $body
    }
    & $Register 'r4-sample-256-astral-scalars-positive' {
        $d=New-GoR4SampleData;$d.values.participant=([char]::ConvertFromUtf32(0x1F642)*256)
        Invoke-GoR4SampleData $d
    }
    & $Register 'r4-sample-257-bmp-scalars-negative' {
        $d=New-GoR4SampleData;$d.values.participant=('A'*257)
        Require-GoCaseRejected {Invoke-GoR4SampleData $d} 'E_GO_SAMPLE_VALUE_LENGTH'
    }
    & $Register 'r4-sample-10000-values-positive' {
        $d=New-GoR4SampleData;$d.values=[ordered]@{}
        for($i=0;$i -lt 10000;$i++){$d.values.Add(('key'+$i),$false)}
        Invoke-GoR4SampleData $d
    }
    & $Register 'r4-sample-10001-values-negative' {
        $d=New-GoR4SampleData;$d.values=[ordered]@{}
        for($i=0;$i -lt 10001;$i++){$d.values.Add(('key'+$i),$false)}
        Require-GoCaseRejected {Invoke-GoR4SampleData $d} 'E_GO_SAMPLE_VALUES'
    }
    & $Register 'r4-sample-duplicate-values-negative' {
        $f=New-GoCommandFixture 'sample-input'
        # Bypass the production strict parser only to prove defense in this assertion.
        # The existing independent Read-GAJson duplicate control remains unchanged.
        $doc=[System.Text.Json.JsonDocument]::Parse('{"schema":"pdfform.data/3","profile":"fictional-workshop@1","values":{"key":"a","key":"b"}}')
        try {
            $f.Payloads['data'].Json=$doc.RootElement
            Require-GoCaseRejected {Assert-GoSampleReceipt (Convert-GoCaseElement $f.Receipt) 0 $f.Template $f.Payloads $f.SavedProfileIdentity} 'E_GO_SAMPLE_VALUES'
        } finally {$doc.Dispose()}
    }
    & $Register 'r4-compatibility-public-fallbacks-positive' {
        $c=New-GoCompatibilityFixture;$c.status='unsupported';$c.exitCode=3
        $c.missingEngineFeatures=@('engine.unsupportedCapability')
        $c.issues=@('E_COMPATIBILITY_ASSESSMENT','W_COMPATIBILITY_ASSESSMENT','E_COMPATIBILITY_ISSUE_LIMIT','W_COMPATIBILITY_ISSUE_LIMIT') |
            ForEach-Object {[ordered]@{code=$_;capability='configuration.assessment'}}
        Assert-GoCompatibility (Convert-GoCaseElement $c) 3 'unsupported' $null
    }
    foreach($field in @('missingEngineFeatures','requiredConfiguration','notAssessed')) {
        $name=$field
        $typed={
            $c=New-GoCompatibilityFixture;$c.status='unsupported';$c.exitCode=3;$c[$name]=@(17)
            Require-GoCaseRejected {Assert-GoCompatibility (Convert-GoCaseElement $c) 3 'unsupported' $null} 'E_GO_STRING'
        }.GetNewClosure()
        & $Register ('r4-compatibility-string-array-'+$field) $typed
        $closed={
            $c=New-GoCompatibilityFixture;$c.status='unsupported';$c.exitCode=3;$c[$name]=@('private_unknown_value')
            Require-GoCaseRejected {Assert-GoCompatibility (Convert-GoCaseElement $c) 3 'unsupported' $null} 'E_GO_COMPATIBILITY_VOCABULARY'
        }.GetNewClosure()
        & $Register ('r4-compatibility-closed-array-'+$field) $closed
    }
    & $Register 'r4-compatibility-array-container-negative' {
        $c=New-GoCompatibilityFixture;$c.requiredConfiguration=[ordered]@{operatorReview=$true}
        Require-GoCaseRejected {Assert-GoCompatibility (Convert-GoCaseElement $c) 1 'configuration_needed' $null} 'E_GO_ARRAY'
    }
    foreach($member in @('code','capability')) {
        $name=$member
        $body={
            $c=New-GoCompatibilityFixture;$c.issues=@([ordered]@{code='E_COMPATIBILITY_ASSESSMENT';capability='configuration.assessment'})
            $c.issues[0][$name]=if($name -ceq 'code'){'E_PRIVATE_ENCODED_VALUE'}else{'private.encoded.value'}
            Require-GoCaseRejected {Assert-GoCompatibility (Convert-GoCaseElement $c) 1 'configuration_needed' $null} 'E_GO_COMPATIBILITY_VOCABULARY'
        }.GetNewClosure()
        & $Register ('r4-compatibility-closed-issue-'+$member) $body
    }
    # The production setup helper, not a separately supplied test token list,
    # installs every private name from the exact accepted fixture generator.
    foreach($encoding in @('raw','escaped')) {
        $kind=$encoding
        $body={
            $s=New-GoR3PrivacyState $AssertionsModule
            & $WorkflowModule {param($s) Add-GAFixturePrivacyTokens $s} $s
            foreach($name in @('grove_entry_alpha','grove_entry_beta','grove_entry_gamma','grove_choice_delta','grove_unsupported_epsilon')) {
                if(-not $s.privacyTokens.Contains($name)){throw 'E_GO_R4_MISSING_FIELD_TOKEN'}
                $wireName=if($kind -ceq 'escaped'){'\u0067'+$name.Substring(1)}else{$name}
                $bytes=[Text.UTF8Encoding]::new($false).GetBytes('{"message":"'+$wireName+'"}')
                Require-GoCaseRejected {& $WorkflowModule {param($s,$b) Add-GADiagnosticReport $s 'synthetic-field-name' $b $true} $s $bytes} 'E_GO_DIAGNOSTIC_VALUE'
            }
        }.GetNewClosure()
        & $Register ('r4-private-field-names-'+$encoding) $body
    }
}
