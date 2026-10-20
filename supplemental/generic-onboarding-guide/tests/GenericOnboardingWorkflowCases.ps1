# SOURCE ONLY / NOT_RUN. No registration/import/fixture build/invocation on load.
# Existing bounded root test host supplies a NEW independently admitted persistent
# context/module/attempt for EACH fixture scenario. No production test-mode switch.
function New-GAPlanCasePaths {
    return [pscustomobject]@{cliDll='C:\attempt\packet\runtime\cli\pdfform.dll';template='C:\attempt\fixtures\third-form.pdf'
        draft='C:\attempt\outputs\configuration\draft';reviewed='C:\attempt\outputs\configuration\reviewed';saved='C:\attempt\outputs\configuration\saved'
        catalog='C:\attempt\outputs\configuration\saved\catalog.json';sample='C:\attempt\outputs\sample-input.json';editable='C:\attempt\outputs\editable.pdf';flattened='C:\attempt\outputs\flattened.pdf'}
}
function Require-GAFixtureRejected([scriptblock]$Body,[string]$Expected) {
    try{& $Body}catch{if($_.Exception.Message -ceq $Expected){return};throw}
    throw 'E_GA_FIXTURE_EXPECTED_REJECTION'
}
function Register-GenericOnboardingWorkflowCases([scriptblock]$Register,$WorkflowModule,[scriptblock]$AdmittedFixtureContextFactory,$PinnedV5Module) {
    # Pure finite-plan/reservation/process-state controls. No child process is used.
    & $Register 'generic-plan-exact-six' {
        $p=New-GAPlanCasePaths;$plan=& $WorkflowModule {param($x) New-GenericOnboardingFixedPlan $x '10.0.13'} $p
        & $WorkflowModule {param($a,$x) Assert-GenericOnboardingFixedPlan $a $x '10.0.13'} $plan $p
        if(($plan.exitCode -join ',') -cne '1,0,0,0,0,0'){throw 'E_GA_FIXTURE_EXITS'}
    }.GetNewClosure()
    foreach($mutation in @('reorder','extra-slot','extra-force','wrong-exit','same-fill-destination')) {
        $kind=$mutation
        $body={
            $p=New-GAPlanCasePaths;$plan=& $WorkflowModule {param($x) New-GenericOnboardingFixedPlan $x '10.0.13'} $p
            $code='E_GA_FIXED_ARGUMENTS'
            switch($kind){
                'reorder'{$first=$plan[0];$plan[0]=$plan[1];$plan[1]=$first;$code='E_GA_FIXED_PLAN'}
                'extra-slot'{$plan+=,$plan[0];$code='E_GA_SIX_FIXED_SLOTS'}
                'extra-force'{$plan[4].argv+=,'--force'}
                'wrong-exit'{$plan[0].exitCode=0;$code='E_GA_FIXED_PLAN'}
                'same-fill-destination'{$plan[5].argv[$plan[5].argv.Count-2]=$p.editable}
            }
            Require-GAFixtureRejected {& $WorkflowModule {param($a,$x) Assert-GenericOnboardingFixedPlan $a $x '10.0.13'} $plan $p} $code
        }.GetNewClosure()
        & $Register ('generic-plan-'+$kind) $body
    }
    & $Register 'generic-reserve-all-six-not-only-next' {
        Require-GAFixtureRejected {& $WorkflowModule {Assert-GAEnvelope 188001L 6}} 'E_GA_REMAINING_RESERVATION'
        & $WorkflowModule {Assert-GAEnvelope 188000L 6}
    }.GetNewClosure()
    & $Register 'generic-reserve-four-after-copy' {
        Require-GAFixtureRejected {& $WorkflowModule {Assert-GAEnvelope 322001L 4}} 'E_GA_REMAINING_RESERVATION'
        & $WorkflowModule {Assert-GAEnvelope 322000L 4}
    }.GetNewClosure()
    & $Register 'generic-hard-600-second-stop' {
        Require-GAFixtureRejected {& $WorkflowModule {Assert-GAEnvelope 600000L 0}} 'E_GA_BATCH_DEADLINE'
        & $WorkflowModule {Assert-GAEnvelope 599999L 0}
    }.GetNewClosure()
    & $Register 'generic-json-fractional-geometry-keeps-numbers-and-strings' {
        $state=[pscustomobject]@{clock=[Diagnostics.Stopwatch]::StartNew();v5=$PinnedV5Module}
        $bytes=[Text.UTF8Encoding]::new($false).GetBytes('{"rectangle":[12.5,40,100.25,60],"label":"123.5"}')
        $json=& $WorkflowModule {param($s,$b) Read-GAJson $s $b} $state $bytes
        if($json.GetProperty('rectangle')[0].GetDouble() -ne 12.5 -or $json.GetProperty('label').GetString() -cne '123.5'){throw 'E_GA_FIXTURE_JSON'}
    }.GetNewClosure()
    & $Register 'generic-json-duplicate-still-rejected' {
        $state=[pscustomobject]@{clock=[Diagnostics.Stopwatch]::StartNew();v5=$PinnedV5Module}
        $bytes=[Text.UTF8Encoding]::new($false).GetBytes('{"number":12.5,"number":40}')
        Require-GAFixtureRejected {& $WorkflowModule {param($s,$b) Read-GAJson $s $b} $state $bytes} 'E_JSON_DUPLICATE_KEY'
    }.GetNewClosure()
    & $Register 'generic-json-lone-surrogate-still-rejected' {
        $state=[pscustomobject]@{clock=[Diagnostics.Stopwatch]::StartNew();v5=$PinnedV5Module}
        $bytes=[Text.UTF8Encoding]::new($false).GetBytes('{"number":12.5,"label":"\uD800"}')
        Require-GAFixtureRejected {& $WorkflowModule {param($s,$b) Read-GAJson $s $b} $state $bytes} 'E_JSON_SURROGATE'
    }.GetNewClosure()
    foreach($fault in @('pending','incomplete-capture','timeout','kill-attempted','wrong-exit')) {
        $kind=$fault
        $body={
            $process=[pscustomobject]@{startConfirmed=$true;terminationObserved=$true;pending=$false;captureComplete=$true;timeout=$false;killAttempted=$false
                primaryCode=$null;observationCode=$null;waitCode=$null;secondaryCodes=[Collections.Generic.List[string]]::new();exitCode=0
                out=[IO.MemoryStream]::new();err=[IO.MemoryStream]::new()}
            try {
                switch($kind){'pending'{$process.pending=$true}'incomplete-capture'{$process.captureComplete=$false}'timeout'{$process.timeout=$true}'kill-attempted'{$process.killAttempted=$true}'wrong-exit'{$process.exitCode=4}}
                Require-GAFixtureRejected {& $WorkflowModule {param($p) Assert-GANormalProcess $p 0} $process} 'E_GA_PROCESS_OUTCOME'
            }finally{$process.out.Dispose();$process.err.Dispose()}
        }.GetNewClosure()
        & $Register ('generic-process-'+$kind) $body
    }
    # Actual six-child integration scenarios using GenericAdapterFixture.cs and
    # the REAL unchanged v5 supervisor, oracle and public completion reconciler.
    # Factory must freeze the fixture CLI closure BEFORE creating its synthetic
    # template/scenario/reviewed files; fixture admission never counts as product acceptance.
    $scenarios=@(
        @('good',6,$true),@('wrong-check-template',1,$false),@('staged-config',2,$false),@('save-exit4-published',3,$false),
        @('mutate-reviewed-after-save',3,$false),@('wrong-document-path',5,$false),@('mutate-sample-after-editable',5,$false),@('mutate-earlier-pdf-after-flattened',6,$false),
        @('nested-value-field',5,$false),@('warning-value-leak',5,$false),@('escaped-warning-value-leak',5,$false),@('late-discovered-value-leak',4,$false),
        @('no-source-draft-cut',2,$false),@('draft-provenance-mismatch',2,$false),@('unrelated-reviewed-copy',2,$false),@('wrong-check-state',1,$false))
    foreach($scenario in $scenarios) {
        $name=$scenario[0];$count=$scenario[1];$success=$scenario[2]
        $body={
            $context=& $AdmittedFixtureContextFactory $name
            $module=$context.workflowModule
            $terminal=& $module {param($c) Invoke-GenericOnboardingWorkflow $c} $context
            $state=& $module {Get-GAState}
            if($terminal.complete -ne $success -or $terminal.executed.Count -ne $count -or $terminal.pendingOwnedProcesses -ne 0 -or $terminal.acceptance){throw 'E_GA_FIXTURE_TERMINAL'}
            if($terminal.visualAcceptance -cne 'not_observed' -or $terminal.perFieldWidgetVerification -cne 'not_observed' -or $terminal.editableSaveReopen -cne 'not_observed'){throw 'E_GA_FIXTURE_FALSE_MANUAL_CLAIM'}
            $gateCase=$name -cin @('no-source-draft-cut','draft-provenance-mismatch','unrelated-reviewed-copy')
            if(-not $success -and ((-not $gateCase -and $state.records[-1].passed) -or [string]::IsNullOrEmpty($state.code))){throw 'E_GA_FIXTURE_FAILURE_NOT_RETAINED'}
            if($gateCase -and ($state.pins.Contains('reviewed-copy/catalog') -or $state.pins.Contains('reviewed-copy/profile'))){throw 'E_GA_FIXTURE_UNPROVEN_COPY'}
            if($name -ceq 'no-source-draft-cut' -and ($terminal.status -cne 'configuration_needed' -or $terminal.exitCode -ne 1 -or -not $terminal.reviewNeeded)){throw 'E_GA_FIXTURE_REVIEW_NEEDED'}
            if($name -cin @('draft-provenance-mismatch','unrelated-reviewed-copy') -and ($terminal.reviewGate -cne 'failed' -or $terminal.exitCode -ne 2)){throw 'E_GA_FIXTURE_PROVENANCE_REFUSAL'}
            foreach($record in $state.records){
                if(-not (& $context.v5Module {param($p) $script:Custody.Contains($p)} $record.process)){throw 'E_GA_FIXTURE_NOT_V5_CUSTODY'}
            }
            if($success){
                if($state.records.Count -ne 6 -or @($state.records|Where-Object{-not $_.passed}).Count -ne 0 -or
                   @($state.records|ForEach-Object{[string]$_.process.pid+'|'+$_.process.processStartUtc}|Select-Object -Unique).Count -ne 6){throw 'E_GA_FIXTURE_FRESH_SIX'}
                foreach($key in @('reviewed-copy/catalog','reviewed-copy/profile','generated-sample','go-fill-editable','go-fill-flattened')){if(-not $state.pins.Contains($key)){throw 'E_GA_FIXTURE_MISSING_PIN'}}
                if($state.lastDomain -eq $null -or -not $state.lastDomain.Complete){throw 'E_GA_FIXTURE_FINAL_DOMAIN'}
            }
            # Unknown/live custody never triggers a reset/reimport/retry here.
        }.GetNewClosure()
        & $Register ('generic-v5-integration-'+$name) $body
    }
}
