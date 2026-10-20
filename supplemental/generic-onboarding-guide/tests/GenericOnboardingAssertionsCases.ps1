# SOURCE ONLY / NOT_RUN. No import, registration, native launch or file I/O on load.
# Root's existing bounded test owner loads the exact pinned assertion module, then
# explicitly calls Register-GenericOnboardingAssertionCases with its own registrar.

function Convert-GoCaseElement($Value) {
    $json=ConvertTo-Json -InputObject $Value -Depth 24 -Compress
    $document=[System.Text.Json.JsonDocument]::Parse($json)
    try { return $document.RootElement.Clone() } finally { $document.Dispose() }
}
function Require-GoCaseRejected([scriptblock]$Body,[string]$Code) {
    try { & $Body } catch { if($_.Exception.Message -ceq $Code){return};throw }
    throw 'E_GO_CASE_EXPECTED_REJECTION'
}
function New-GoConfigurationFixture([string]$Operation='configure') {
    $a='a'*64;$b='b'*64;$c='c'*64
    $profile=Convert-GoCaseElement ([ordered]@{schema='pdfform.profile/3';id='fictional-workshop';revision='1';profileVersion=1})
    $payloads=[ordered]@{
        catalog=[pscustomobject]@{LogicalPath='catalog.json';Bytes=5L;Sha256=$a}
        profile=[pscustomobject]@{LogicalPath='profile.json';Bytes=64L;Sha256=$b;Json=$profile}
    }
    $outputs=@([ordered]@{id='catalog';path='catalog.json';bytes=5L;sha256=$a},[ordered]@{id='profile';path='profile.json';bytes=64L;sha256=$b})
    $members=@('catalog.json','profile.json','result.json','completion.json')
    $templatePath='approved-template.pdf'
    if($Operation -ceq 'save-config') {
        $templatePath='template.pdf'
        $payloads.Add('template',[pscustomobject]@{LogicalPath='template.pdf';Bytes=128L;Sha256=$c})
        $outputs+=,[ordered]@{id='template';path='template.pdf';bytes=128L;sha256=$c};$members+=,'template.pdf'
    }
    $receipt=[ordered]@{
        schema='pdfform.configuration-result/1';operation=$Operation;status='ok';exitCode=0;artifactState='published'
        validationLevels=[ordered]@{syntax='passed';profile='passed';binding='not_run';bindingStructure='not_run';data='not_run';packet='not_run';render='not_run'}
        template=[ordered]@{path=$templatePath;bytes=128L;sha256=$c}
        profile=[ordered]@{path='profile.json';bytes=64L;sha256=$b;id='fictional-workshop';revision='1';profileVersion=1}
        outputs=$outputs;errors=@();warnings=@();filingReadiness='not_assessed'
    }
    return [pscustomobject]@{Receipt=$receipt;Operation=$Operation;ProcessExit=0;Template=[pscustomobject]@{Bytes=128L;Sha256=$c};Payloads=$payloads;Members=$members}
}
function Invoke-GoConfigurationFixture($Fixture) {
    Assert-GoConfigurationReceipt (Convert-GoCaseElement $Fixture.Receipt) $Fixture.ProcessExit $Fixture.Operation $Fixture.Template 'approved-template.pdf' $Fixture.Payloads $Fixture.Members $true
}
function New-GoCommandFixture([string]$Operation) {
    $template=[pscustomobject]@{Bytes=128L;Sha256=('c'*64)}
    $receipt=[ordered]@{
        schema='pdfform.result/2';operation=$Operation;status='ok';exitCode=0;artifactState='published'
        validationLevels=[ordered]@{syntax='passed';profile='passed';binding='passed';bindingStructure='passed';data='passed';packet='not_run';render='not_run'}
        tool=[ordered]@{name='pdfform';version='0.1.0-dev';build='development-local';engine='iText 9.8.0';runtime='.NET 10.0.13';sourceReference='unpublished-source'}
        profiles=@();templates=@([ordered]@{id='template';sha256=$template.Sha256})
        outputs=@();errors=@();warnings=@();stats=[ordered]@{};filingReadiness='not_assessed'
    }
    $payloads=[ordered]@{}
    if($Operation -ceq 'sample-input') {
        $receipt.outputs=@([ordered]@{id='data';path='data.json';bytes=32L;sha256=('a'*64)})
        $payloads.Add('data',[pscustomobject]@{LogicalPath='data.json';Bytes=32L;Sha256=('a'*64);Json=(Convert-GoCaseElement ([ordered]@{schema='pdfform.data/3';profile='fictional-workshop@1';values=[ordered]@{participant='FICTIONAL MAPLE MAKER'}}))})
    } else {
        $receipt.validationLevels.render='passed'
        $receipt.outputs=@([ordered]@{id='document';path='document.pdf';bytes=256L;sha256=('d'*64)})
        $payloads.Add('document',[pscustomobject]@{LogicalPath='document.pdf';Bytes=256L;Sha256=('d'*64)})
    }
    return [pscustomobject]@{Receipt=$receipt;Template=$template;Payloads=$payloads;SavedProfileIdentity='fictional-workshop@1'}
}
function New-GoCompatibilityFixture {
    return [ordered]@{schema='pdfform.compatibility/1';status='configuration_needed';exitCode=1;templateSha256=('c'*64);templateBytes=128L
        counts=[ordered]@{pages=1L;terminalFields=0L;textFields=0L;checkboxFields=0L;otherFields=0L;widgets=0L;requiredFields=0L;combFields=0L}
        configurationState='absent';issues=@();missingEngineFeatures=@();requiredConfiguration=@('operatorReview');notAssessed=@('viewerAcceptance')}
}
function New-GoFreezeFixture {
    return [ordered]@{sequence=1L;launcherSha256=('e'*64);files=@(
        [ordered]@{relativePath='engine/.';directory=$true;nativeIdentity='VOL:ROOT';bytes=0L;sha256=$null},
        [ordered]@{relativePath='engine/pdfform.dll';directory=$false;nativeIdentity='VOL:FILE';bytes=256L;sha256=('f'*64)})}
}
function Register-GenericOnboardingAssertionCases([scriptblock]$Register) {
    & $Register 'configuration-configure-exact-success' { Invoke-GoConfigurationFixture (New-GoConfigurationFixture) }
    & $Register 'configuration-save-exact-success' { Invoke-GoConfigurationFixture (New-GoConfigurationFixture 'save-config') }
    $mutations=@(
        @('configuration-staged',{param($f)$f.Receipt.artifactState='staged'},'E_GO_TEXT_MISMATCH'),
        @('configuration-unknown',{param($f)$f.Receipt.artifactState='unknown'},'E_GO_TEXT_MISMATCH'),
        @('configuration-wrong-operation',{param($f)$f.Receipt.operation='save-config'},'E_GO_TEXT_MISMATCH'),
        @('configuration-wrong-status',{param($f)$f.Receipt.status='supported'},'E_GO_TEXT_MISMATCH'),
        @('configuration-nonzero-receipt',{param($f)$f.Receipt.exitCode=4},'E_GO_CONFIGURATION_TERMINAL'),
        @('configuration-published-nonzero-process',{param($f)$f.ProcessExit=4},'E_GO_CONFIGURATION_TERMINAL'),
        @('configuration-extra-payload',{param($f)$f.Receipt.outputs+=,[ordered]@{id='unrelated';path='other.json';bytes=1L;sha256=('a'*64)}},'E_GO_ARRAY'),
        @('configuration-missing-payload',{param($f)$f.Receipt.outputs=@($f.Receipt.outputs[0])},'E_GO_ARRAY'),
        @('configuration-duplicate-payload',{param($f)$f.Receipt.outputs=@($f.Receipt.outputs[0],$f.Receipt.outputs[0])},'E_GO_PAYLOAD_SET'),
        @('configuration-wrong-logical-path',{param($f)$f.Receipt.outputs[0].path='other.json'},'E_GO_PAYLOAD_IDENTITY'),
        @('configuration-wrong-hash',{param($f)$f.Receipt.outputs[0].sha256=('9'*64)},'E_GO_PAYLOAD_IDENTITY'),
        @('configuration-wrong-length',{param($f)$f.Receipt.outputs[0].bytes=6L},'E_GO_PAYLOAD_IDENTITY'),
        @('configuration-string-length',{param($f)$f.Receipt.outputs[0].bytes='5'},'E_GO_INTEGER'),
        @('configuration-zero-length',{param($f)$f.Receipt.outputs[0].bytes=0L},'E_GO_INTEGER'),
        @('configuration-wrong-template',{param($f)$f.Receipt.template.sha256=('8'*64)},'E_GO_TEMPLATE_IDENTITY'),
        @('configuration-wrong-profile-id',{param($f)$f.Receipt.profile.id='other'},'E_GO_PROFILE_IDENTITY'),
        @('configuration-wrong-profile-version',{param($f)$f.Receipt.profile.profileVersion=2},'E_GO_PROFILE_IDENTITY'),
        @('configuration-data-claimed',{param($f)$f.Receipt.validationLevels.data='passed'},'E_GO_VALIDATION_LEVEL'),
        @('configuration-extra-directory-member',{param($f)$f.Members+=,'unrelated.json'},'E_GO_CONFIGURATION_MEMBERS'),
        @('configuration-missing-completion',{param($f)$f.Members=@('catalog.json','profile.json','result.json')},'E_GO_CONFIGURATION_MEMBERS'),
        @('configuration-extra-top-member',{param($f)$f.Receipt.Add('unrelated','value')},'E_GO_OBJECT_MEMBERS')
    )
    foreach($case in $mutations) {
        $mutation=$case[1];$expected=$case[2]
        $body={ $fixture=New-GoConfigurationFixture;& $mutation $fixture;Require-GoCaseRejected {Invoke-GoConfigurationFixture $fixture} $expected }.GetNewClosure()
        & $Register $case[0] $body
    }
    & $Register 'configuration-completion-not-accepted' {
        $f=New-GoConfigurationFixture
        Require-GoCaseRejected {Assert-GoConfigurationReceipt (Convert-GoCaseElement $f.Receipt) 0 'configure' $f.Template 'approved-template.pdf' $f.Payloads $f.Members $false} 'E_GO_CONFIGURATION_TERMINAL'
    }
    & $Register 'configuration-duplicate-json-member' {
        $f=New-GoConfigurationFixture;$json=ConvertTo-Json -InputObject $f.Receipt -Depth 24 -Compress
        $json=$json.Replace('"status":"ok"','"status":"ok","status":"ok"')
        $document=[System.Text.Json.JsonDocument]::Parse($json)
        try { Require-GoCaseRejected {Assert-GoConfigurationReceipt $document.RootElement 0 'configure' $f.Template 'approved-template.pdf' $f.Payloads $f.Members $true} 'E_GO_OBJECT_MEMBERS' } finally {$document.Dispose()}
    }
    & $Register 'sample-separate-command-result' {
        $f=New-GoCommandFixture 'sample-input';Assert-GoSampleReceipt (Convert-GoCaseElement $f.Receipt) 0 $f.Template $f.Payloads $f.SavedProfileIdentity
    }
    & $Register 'sample-configuration-schema-denied' {
        $f=New-GoCommandFixture 'sample-input';$f.Receipt.schema='pdfform.configuration-result/1'
        Require-GoCaseRejected {Assert-GoSampleReceipt (Convert-GoCaseElement $f.Receipt) 0 $f.Template $f.Payloads $f.SavedProfileIdentity} 'E_GO_TEXT_MISMATCH'
    }
    & $Register 'sample-wrong-logical-path' {
        $f=New-GoCommandFixture 'sample-input';$f.Receipt.outputs[0].path='sample.json'
        Require-GoCaseRejected {Assert-GoSampleReceipt (Convert-GoCaseElement $f.Receipt) 0 $f.Template $f.Payloads $f.SavedProfileIdentity} 'E_GO_PAYLOAD_IDENTITY'
    }
    & $Register 'fill-exact-document-path' {
        $f=New-GoCommandFixture 'fill';Assert-GoPublishedPdfReceipt (Convert-GoCaseElement $f.Receipt) 0 $f.Template $f.Payloads
    }
    & $Register 'fill-wrong-document-path' {
        $f=New-GoCommandFixture 'fill';$f.Receipt.outputs[0].path='different.pdf'
        Require-GoCaseRejected {Assert-GoPublishedPdfReceipt (Convert-GoCaseElement $f.Receipt) 0 $f.Template $f.Payloads} 'E_GO_PAYLOAD_IDENTITY'
    }
    & $Register 'check-exact-compatibility-identity' {
        Assert-GoCompatibility (Convert-GoCaseElement (New-GoCompatibilityFixture)) 1 'configuration_needed' ([pscustomobject]@{Bytes=128L;Sha256=('c'*64)})
    }
    & $Register 'check-ordinary-result-schema-denied' {
        $f=New-GoCompatibilityFixture;$f.schema='pdfform.result/2'
        Require-GoCaseRejected {Assert-GoCompatibility (Convert-GoCaseElement $f) 1 'configuration_needed' ([pscustomobject]@{Bytes=128L;Sha256=('c'*64)})} 'E_GO_TEXT_MISMATCH'
    }
    & $Register 'check-wrong-observed-template-denied' {
        $f=New-GoCompatibilityFixture;$f.templateSha256=('7'*64)
        Require-GoCaseRejected {Assert-GoCompatibility (Convert-GoCaseElement $f) 1 'configuration_needed' ([pscustomobject]@{Bytes=128L;Sha256=('c'*64)})} 'E_GO_TEMPLATE_IDENTITY'
    }
    foreach($state in @('absent','draft','reviewed','invalid')) {
        $configurationState=$state
        $body={
            $f=New-GoCompatibilityFixture;$f.status='invalid';$f.exitCode=2;$f.configurationState=$configurationState
            Assert-GoCompatibility (Convert-GoCaseElement $f) 2 'invalid' ([pscustomobject]@{Bytes=128L;Sha256=('c'*64)})
        }.GetNewClosure()
        & $Register ('check-public-configuration-state-'+$state) $body
    }
    & $Register 'check-public-preobservation-failure-omits-identity' {
        $f=New-GoCompatibilityFixture;$f.status='internal_error';$f.exitCode=4;$f.configurationState='invalid';$f.counts=[ordered]@{}
        $f.Remove('templateSha256');$f.Remove('templateBytes')
        Assert-GoCompatibility (Convert-GoCaseElement $f) 4 'internal_error' $null
    }
    & $Register 'check-public-optional-identity-still-required-by-approved-workflow-pin' {
        $f=New-GoCompatibilityFixture;$f.Remove('templateSha256');$f.Remove('templateBytes')
        Require-GoCaseRejected {Assert-GoCompatibility (Convert-GoCaseElement $f) 1 'configuration_needed' ([pscustomobject]@{Bytes=128L;Sha256=('c'*64)})} 'E_GO_TEMPLATE_IDENTITY'
    }
    $compatibilityMutations=@(
        @('unavailable-state',{param($f)$f.configurationState='unavailable'},'E_GO_CONFIGURATION_STATE'),
        @('counts-not-object',{param($f)$f.counts=@()},'E_GO_OBJECT'),
        @('negative-count',{param($f)$f.counts.pages=-1L},'E_GO_INTEGER'),
        @('fractional-count',{param($f)$f.counts.pages=1.5},'E_GO_INTEGER'),
        @('string-count',{param($f)$f.counts.pages='1'},'E_GO_INTEGER'),
        @('private-count-name',{param($f)$f.counts.Add('PRIVATE_FIELD_NAME',1L)},'E_GO_OBJECT_MEMBERS'),
        @('private-pages',{param($f)$f.Add('pages',1L)},'E_GO_OBJECT_MEMBERS'),
        @('private-prefilled',{param($f)$f.Add('prefilled',$false)},'E_GO_OBJECT_MEMBERS'),
        @('private-fields',{param($f)$f.Add('fields',@())},'E_GO_OBJECT_MEMBERS'),
        @('private-issue-message',{param($f)$f.issues=@([ordered]@{code='E_COMPATIBILITY_ASSESSMENT';capability='configuration.assessment';message='PRIVATE_FACT'})},'E_GO_OBJECT_MEMBERS'),
        @('public-prefilled-issue',{param($f)$f.issues=@([ordered]@{code='E_TEMPLATE_PREFILLED';capability='form.blankOriginal'})},'E_GO_NOT_BLANK')
    )
    foreach($case in $compatibilityMutations) {
        $mutation=$case[1];$expected=$case[2]
        $body={
            $f=New-GoCompatibilityFixture;& $mutation $f
            Require-GoCaseRejected {Assert-GoCompatibility (Convert-GoCaseElement $f) 1 'configuration_needed' ([pscustomobject]@{Bytes=128L;Sha256=('c'*64)})} $expected
        }.GetNewClosure()
        & $Register ('check-public-'+$case[0]) $body
    }
    & $Register 'protected-pdf-changed-after-render-denied' {
        $pin=[pscustomobject]@{Path='C:\outputs\original.pdf';Bytes=256L;Sha256=('d'*64);NativeIdentity='VOL:PDF'}
        $observed=[pscustomobject]@{Path=$pin.Path;Bytes=256L;Sha256=('1'*64);NativeIdentity=$pin.NativeIdentity}
        Require-GoCaseRejected {Assert-GoObservedPin $pin $observed} 'E_GO_PROTECTED_ARTIFACT_CHANGED'
    }
    & $Register 'copied-input-changed-after-invocation-denied' {
        $pin=[pscustomobject]@{Path='C:\configuration\reviewed\profile.json';Bytes=64L;Sha256=('b'*64);NativeIdentity='VOL:PROFILE'}
        $changed=[pscustomobject]@{Path=$pin.Path;Bytes=65L;Sha256=$pin.Sha256;NativeIdentity=$pin.NativeIdentity}
        Require-GoCaseRejected {Assert-GoProtectedSet ([ordered]@{copy=$pin}) ([ordered]@{copy=$changed})} 'E_GO_PROTECTED_ARTIFACT_CHANGED'
    }
    & $Register 'final-settlement-missing-protected-input-denied' {
        Require-GoCaseRejected {Assert-GoProtectedSet ([ordered]@{synthetic='original-pin'}) ([ordered]@{})} 'E_GO_PROTECTED_SET'
    }
    & $Register 'freeze-before-introduction' {
        Assert-GoFrozenClosure (Convert-GoCaseElement (New-GoFreezeFixture)) (Convert-GoCaseElement (New-GoFreezeFixture)) 2L
    }
    & $Register 'freeze-after-introduction-denied' {
        Require-GoCaseRejected {Assert-GoFrozenClosure (Convert-GoCaseElement (New-GoFreezeFixture)) (Convert-GoCaseElement (New-GoFreezeFixture)) 1L} 'E_GO_FREEZE_INTRODUCTION_ORDER'
    }
    & $Register 'freeze-changed-closure-denied' {
        $f=New-GoFreezeFixture;$f.files[1].sha256=('0'*64)
        Require-GoCaseRejected {Assert-GoFrozenClosure (Convert-GoCaseElement (New-GoFreezeFixture)) (Convert-GoCaseElement $f) 2L} 'E_GO_FROZEN_CLOSURE_CHANGED'
    }
    & $Register 'fixture-inside-bundle-denied' {
        $roots=[ordered]@{bundle=[pscustomobject]@{Path='C:\bundle';NativeIdentity='VOL:B'};fixtures=[pscustomobject]@{Path='C:\bundle\fixtures';NativeIdentity='VOL:F'};configuration=[pscustomobject]@{Path='C:\configuration';NativeIdentity='VOL:C'};evidence=[pscustomobject]@{Path='C:\evidence';NativeIdentity='VOL:E'}}
        Require-GoCaseRejected {Assert-GoDisjointRoots $roots} 'E_GO_ROOTS_OVERLAP'
    }
}
