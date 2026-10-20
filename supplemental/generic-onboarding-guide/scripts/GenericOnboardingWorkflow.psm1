#requires -Version 7.4
# SOURCE ONLY / NOT_RUN. Fixed six-slot adapter, not a process supervisor.
# Uses the already loaded, pinned v5 New-ProcessState/Invoke-OwnedProcess and custody.
# No imports, assembly load, compilation, reset, new kill protocol or arbitrary command API.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$script:GA=$null
$script:V5Hash='298603d591eeeddddd8763dc19ac47949ba390f570873fc3cfed6da8c6c3ee29'
$script:GAIds=@('go-check','go-configure','go-save-config','go-sample-input','go-fill-editable','go-fill-flattened')

function Get-GACode($Failure) {
    if($Failure.Exception.Message -cmatch '\AE_[A-Z0-9_]{1,64}\z'){return $Failure.Exception.Message}
    return 'E_GA_OPERATION'
}
function Assert-GAEnvelope([long]$Elapsed,[int]$RemainingSlots) {
    if($Elapsed -lt 0 -or $Elapsed -ge 600000){throw 'E_GA_BATCH_DEADLINE'}
    # Reserve ALL remaining 60+5+2 slots, plus finite final accounting/write time.
    if($RemainingSlots -lt 0 -or $RemainingSlots -gt 6 -or ($RemainingSlots -gt 0 -and 600000-$Elapsed -lt (67000L*$RemainingSlots+10000L))){throw 'E_GA_REMAINING_RESERVATION'}
}
function Assert-GAClock($State,[int]$RemainingSlots=0) {
    if($State.clock -isnot [Diagnostics.Stopwatch] -or -not $State.clock.IsRunning){throw 'E_GA_BATCH_DEADLINE'}
    Assert-GAEnvelope $State.clock.ElapsedMilliseconds $RemainingSlots
}
function New-GenericOnboardingFixedPlan($Paths,[string]$RuntimeVersion) {
    if($RuntimeVersion -cne '10.0.13'){throw 'E_GA_RUNTIME_VERSION'}
    $prefix=@('exec','--fx-version','10.0.13','--roll-forward','Disable',$Paths.cliDll)
    return ,@(
        [pscustomobject]@{id='go-check';operation='check';exitCode=1;schema='pdfform.compatibility/1';status='configuration_needed';artifactState='none';argv=$prefix+@('check',$Paths.template)},
        [pscustomobject]@{id='go-configure';operation='configure';exitCode=0;schema='pdfform.configuration-result/1';status='ok';artifactState='published';argv=$prefix+@('configure',$Paths.template,'--out',$Paths.draft)},
        [pscustomobject]@{id='go-save-config';operation='save-config';exitCode=0;schema='pdfform.configuration-result/1';status='ok';artifactState='published';argv=$prefix+@('save-config',$Paths.template,'--draft',$Paths.reviewed,'--out',$Paths.saved,'--reviewed')},
        [pscustomobject]@{id='go-sample-input';operation='sample-input';exitCode=0;schema='pdfform.result/2';status='ok';artifactState='published';argv=$prefix+@('sample-input','--catalog',$Paths.catalog,'--out',$Paths.sample)},
        [pscustomobject]@{id='go-fill-editable';operation='fill';exitCode=0;schema='pdfform.result/2';status='ok';artifactState='published';argv=$prefix+@('fill',$Paths.template,'--catalog',$Paths.catalog,'--data',$Paths.sample,'--out',$Paths.editable)},
        [pscustomobject]@{id='go-fill-flattened';operation='fill';exitCode=0;schema='pdfform.result/2';status='ok';artifactState='published';argv=$prefix+@('fill',$Paths.template,'--catalog',$Paths.catalog,'--data',$Paths.sample,'--out',$Paths.flattened,'--flatten')})
}
function Assert-GenericOnboardingFixedPlan($Actual,$Paths,[string]$RuntimeVersion) {
    $expected=New-GenericOnboardingFixedPlan $Paths $RuntimeVersion
    if($Actual -isnot [object[]] -or $Actual.Count -ne 6){throw 'E_GA_SIX_FIXED_SLOTS'}
    for($i=0;$i -lt 6;$i++) {
        foreach($key in @('id','operation','exitCode','schema','status','artifactState')){if($Actual[$i].$key -cne $expected[$i].$key){throw 'E_GA_FIXED_PLAN'}}
        if($Actual[$i].argv -isnot [object[]] -or $Actual[$i].argv.Count -ne $expected[$i].argv.Count){throw 'E_GA_FIXED_ARGUMENTS'}
        for($j=0;$j -lt $expected[$i].argv.Count;$j++){if($Actual[$i].argv[$j] -isnot [string] -or $Actual[$i].argv[$j] -cne $expected[$i].argv[$j]){throw 'E_GA_FIXED_ARGUMENTS'}}
    }
}
function Get-GAState {
    if($null -eq $script:GA){throw 'E_GA_NOT_INITIALIZED'}
    if($script:GA.ownerPid -ne $PID -or $script:GA.runspaceId -ne [runspace]::DefaultRunspace.InstanceId){throw 'E_GA_PERSISTENT_SESSION_REQUIRED'}
    return $script:GA
}
function Read-GAFile($State,[string]$Path,[long]$Maximum,[bool]$Capture=$false) {
    Assert-GAClock $State
    & $State.v5 {param($p) Assert-AbsoluteLocalPath $p;Assert-ExistingFixedPath $p $false} $Path
    $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $hash=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    $memory=if($Capture){[IO.MemoryStream]::new()}else{$null}
    try {
        $identity=$State.context.domainOracle.ReadIdentity($stream.SafeFileHandle)
        $native=('{0:x16}:{1:x16}' -f $identity.Volume,$identity.Number)
        $length=$stream.Length
        if($length -lt 0 -or $length -gt $Maximum){throw 'E_GA_FILE_CAP'}
        $buffer=[byte[]]::new(8192);[long]$read=0
        while(($count=$stream.Read($buffer,0,$buffer.Length)) -gt 0) {
            Assert-GAClock $State;$read+=$count;$State.readBytes+=$count
            if($read -gt $length -or $State.readBytes -gt 268435456){throw 'E_GA_READ_CAP'}
            $hash.AppendData($buffer,0,$count);if($Capture){$memory.Write($buffer,0,$count)}
        }
        $again=$State.context.domainOracle.ReadIdentity($stream.SafeFileHandle)
        if($read -ne $length -or $stream.Length -ne $length -or $identity.Volume -ne $again.Volume -or $identity.Number -ne $again.Number){throw 'E_GA_OPEN_FILE_CHANGED'}
        $value=[pscustomobject]@{Path=$Path;Bytes=$length;Sha256=[Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant();NativeIdentity=$native;Data=$null}
        if($Capture){$value.Data=$memory.ToArray()}
        return $value
    } finally {$hash.Dispose();$stream.Dispose();if($null -ne $memory){$memory.Dispose()}}
}
function Read-GAJson($State,[byte[]]$Bytes,[int]$Maximum=262144) {
    if($Maximum -gt 1048576 -or $Bytes.Length -eq 0 -or $Bytes.Length -gt $Maximum){throw 'E_GA_RECEIPT_CAP'}
    $text=[Text.UTF8Encoding]::new($false,$true).GetString($Bytes)
    $options=[System.Text.Json.JsonDocumentOptions]::new();$options.MaxDepth=24
    $document=[System.Text.Json.JsonDocument]::Parse($text,$options)
    try {
        # Compatibility/profile geometry contains finite fractional numbers. v5's
        # integer-only converter cannot consume those directly. Quote only numeric
        # TOKENS in already syntax-checked JSON, preserving every original string,
        # duplicate member and surrogate escape for v5's strict evidence checks.
        $pattern='"(?:[^"\\\x00-\x1f]|\\(?:["\\/bfnrt]|u[0-9a-fA-F]{4}))*"|(?<number>-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)'
        $tokens=[regex]::Matches($text,$pattern)
        $quoted=[Text.StringBuilder]::new();$offset=0
        foreach($token in $tokens) {
            Assert-GAClock $State
            $null=$quoted.Append($text,$offset,$token.Index-$offset)
            if($token.Groups['number'].Success) {
                $number=[double]::Parse($token.Value,[Globalization.CultureInfo]::InvariantCulture)
                if(-not [double]::IsFinite($number)){throw 'E_GA_JSON_NUMBER'}
                $null=$quoted.Append('"').Append($token.Value).Append('"')
            }else{$null=$quoted.Append($token.Value)}
            $offset=$token.Index+$token.Length
            if($quoted.Length -gt 1048576){throw 'E_GA_JSON_CAP'}
        }
        $null=$quoted.Append($text,$offset,$text.Length-$offset)
        $strictBytes=[Text.UTF8Encoding]::new($false,$true).GetBytes($quoted.ToString())
        $null=& $State.v5 {param($b) Read-StrictJson $b} $strictBytes
        return $document.RootElement.Clone()
    }finally{$document.Dispose()}
}
function Add-GAPrivacyToken($State,[string]$Token) {
    # Same discriminating-value rule as task11: booleans and strings shorter
    # than four characters are not unique canaries. Exact envelope shapes still
    # deny injected value fields. No token/value is emitted in failure messages.
    if([string]::IsNullOrWhiteSpace($Token) -or $Token.Length -lt 4 -or $Token -iin @('true','false')) { return }
    if($Token.Length -gt 4096) { throw 'E_GA_PRIVACY_TOKEN_CAP' }
    if($State.privacyTokens.Add($Token)) {
        $State.privacyChars+=$Token.Length
        if($State.privacyTokens.Count -gt 512 -or $State.privacyChars -gt 65536) { throw 'E_GA_PRIVACY_TOKEN_CAP' }
    }
}
function Add-GAFixturePrivacyTokens($State) {
    # The actual accepted generator's private PDF names are mandatory, including
    # the negative-only name. Keep the same bounded token insertion path.
    foreach($token in @('PRIVATE_SENTINEL','PRIVATE_DRIVER','FICTIONAL OLD ENTRY','FICTIONAL MAPLE MAKER','DEMO-314','2031-05-17',
        'grove_entry_alpha','grove_entry_beta','grove_entry_gamma','grove_choice_delta','grove_unsupported_epsilon')) { Add-GAPrivacyToken $State $token }
}
function Assert-GAStoredReports($State) {
    foreach($report in $State.reports) {
        Assert-GAClock $State
        & $State.assertions {param($b,$json,$tokens,$clock) Assert-GoDiagnosticReport $b $json $tokens $clock} $report.Bytes $report.Json @($State.privacyTokens) $State.clock
    }
}
function Add-GADiagnosticReport($State,[string]$Channel,[byte[]]$Bytes,[bool]$Json) {
    if($State.reports.Count -ge 64 -or $State.reportBytes+$Bytes.Length -gt 8388608) { throw 'E_GA_REPORT_CAPTURE_CAP' }
    $copy=[byte[]]$Bytes.Clone()
    & $State.assertions {param($b,$json,$tokens,$clock) Assert-GoDiagnosticReport $b $json $tokens $clock} $copy $Json @($State.privacyTokens) $State.clock
    $State.reports.Add([pscustomobject]@{Channel=$Channel;Bytes=$copy;Json=$Json});$State.reportBytes+=$copy.Length
}
function Add-GASamplePrivacyValues($State,[System.Text.Json.JsonElement]$Data) {
    # Explicitly inspect values in the intentional export only to learn tokens.
    # The export itself is NEVER submitted to a diagnostic leak checker.
    $pending=[Collections.Generic.Stack[System.Text.Json.JsonElement]]::new();$pending.Push($Data.GetProperty('values'));$nodes=0
    while($pending.Count -gt 0) {
        Assert-GAClock $State
        if(++$nodes -gt 16384) { throw 'E_GA_SAMPLE_VALUE_SCAN_CAP' }
        $node=$pending.Pop()
        if($node.ValueKind -eq [System.Text.Json.JsonValueKind]::String) { Add-GAPrivacyToken $State $node.GetString() }
        elseif($node.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
            foreach($property in $node.EnumerateObject()) {$pending.Push($property.Value);if($pending.Count -gt 16384){throw 'E_GA_SAMPLE_VALUE_SCAN_CAP'}}
        } elseif($node.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
            foreach($child in $node.EnumerateArray()) {$pending.Push($child);if($pending.Count -gt 16384){throw 'E_GA_SAMPLE_VALUE_SCAN_CAP'}}
        }
    }
    # Earlier reports may have disclosed a value before the generator returned it.
    Assert-GAStoredReports $State
}
function Assert-GADomain($State) {
    Assert-GAClock $State
    $observed=$State.context.domainOracle.ObserveDomain()
    if(-not $observed.Complete -or $observed.InputInventorySha256 -cne $State.context.inputInventorySha256 -or
       $observed.ApparentBytes -gt $State.context.maximumRunBytes -or $observed.AllocatedBytes -gt $State.context.maximumRunBytes -or
       $observed.FreeBytes -lt $State.context.reserveBytes){throw 'E_GA_DOMAIN'}
    $expected=@($State.entries|Sort-Object -CaseSensitive)
    if($observed.ArtifactEntries.Count -ne $expected.Count){throw 'E_GA_DOMAIN_ENTRIES'}
    for($i=0;$i -lt $expected.Count;$i++){if($observed.ArtifactEntries[$i] -cne $expected[$i]){throw 'E_GA_DOMAIN_ENTRIES'}}
    return $observed
}
function Assert-GAInvariants($State) {
    Assert-GAClock $State;$State.readBytes=0L
    $observed=[ordered]@{}
    foreach($key in $State.pins.Keys){$pin=$State.pins[$key];$observed.Add($key,(Read-GAFile $State $pin.Path 67108864))}
    & $State.assertions {param($p,$o) Assert-GoProtectedSet $p $o} $State.pins $observed
    foreach($root in $State.roots.Values){Assert-GAClock $State;if($State.context.task11Host.NativeIdentity($root.Path) -cne $root.NativeIdentity){throw 'E_GA_ROOT_IDENTITY_CHANGED'}}
    foreach($directory in $State.directoryPins.Values){Assert-GAClock $State;if($State.context.task11Host.NativeIdentity($directory.Path) -cne $directory.NativeIdentity){throw 'E_GA_DIRECTORY_IDENTITY_CHANGED'}}
    # Native directory/file identities and complete package input membership are
    # reobserved by the existing oracle; no raw recursive enumeration is added.
    $currentFiles=[Collections.Generic.List[object]]::new()
    foreach($file in $State.freeze.files) {
        $path=$State.freezePaths[$file.relativePath]
        if($file.directory) {
            Assert-GAClock $State
            $native=$State.context.task11Host.NativeIdentity($path)
            $currentFiles.Add([ordered]@{relativePath=$file.relativePath;directory=$true;nativeIdentity=$native;bytes=0L;sha256=$null})
        } else {
            $value=Read-GAFile $State $path 67108864
            $currentFiles.Add([ordered]@{relativePath=$file.relativePath;directory=$false;nativeIdentity=$value.NativeIdentity;bytes=$value.Bytes;sha256=$value.Sha256})
        }
    }
    $current=[ordered]@{sequence=$State.freeze.sequence;files=$currentFiles.ToArray();launcherSha256=$State.freeze.launcherSha256}
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $current -Depth 12 -Compress))
    $currentJson=Read-GAJson $State $bytes
    & $State.assertions {param($f,$c,$n) Assert-GoFrozenClosure $f $c $n} $State.freezeJson $currentJson $State.context.firstFixtureSequence
    $State.lastDomain=Assert-GADomain $State
    Assert-GAStoredReports $State
}
function Add-GAPin($State,[string]$Key,$Observed,[long]$ExpectedBytes,[string]$ExpectedSha256) {
    if($State.pins.Count -ge 2048 -or $State.pins.Contains($Key) -or $Observed.Bytes -ne $ExpectedBytes -or $Observed.Sha256 -cne $ExpectedSha256){throw 'E_GA_NEW_PIN'}
    $State.pins.Add($Key,[pscustomobject]@{Path=$Observed.Path;Bytes=$ExpectedBytes;Sha256=$ExpectedSha256;NativeIdentity=$Observed.NativeIdentity})
}
function Write-GAEvidence($State,[string]$Name,[byte[]]$Bytes) {
    Assert-GAClock $State
    if($Bytes.Length -gt 1048576){throw 'E_GA_EVIDENCE_CAP'}
    if($Name.EndsWith('.process.json',[StringComparison]::Ordinal) -or $Name -ceq 'generic-onboarding.terminal.json') { Add-GADiagnosticReport $State $Name $Bytes $true }
    $path=[IO.Path]::Combine($State.paths.evidence,$Name)
    & $State.v5 {param($p,$b) Write-NewBytes $p $b} $path $Bytes
    $sha=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
    $State.readBytes=0L;$actual=Read-GAFile $State $path 1048576
    Add-GAPin $State ('evidence/'+$Name) $actual $Bytes.Length $sha
}
function Confirm-GAReviewedProvenance($State) {
    Assert-GAInvariants $State
    if(-not $State.pins.Contains('reviewed-draft-provenance')) {
        $State.reviewGate='review_needed';$State.reviewNeeded=$true;$State.stopped=$true;$State.code='E_GA_SOURCE_DRAFT_REVIEW_REQUIRED'
        return $false
    }
    try {
        $original=$State.pins['reviewed-draft-provenance'];$State.readBytes=0L
        $observed=Read-GAFile $State $original.Path 262144 $true
        & $State.assertions {param($p,$o) Assert-GoObservedPin $p $o} $original $observed
        $proof=Read-GAJson $State $observed.Data
        $current=[ordered]@{catalog=$State.pins['configure/catalog'];profile=$State.pins['configure/profile']}
        $reviewed=[ordered]@{catalog=$State.pins['reviewed-source/catalog'];profile=$State.pins['reviewed-source/profile']}
        & $State.assertions {param($p,$t,$cli,$d,$r) Assert-GoReviewedDraftProvenance $p $t $cli $d $r} $proof $State.templatePin $State.pins['cli'].Sha256 $current $reviewed
        Assert-GAInvariants $State
        $State.reviewGate='passed';return $true
    } catch {$State.reviewGate='failed';throw}
}
function Copy-GAReviewed($State) {
    if($State.reviewGate -cne 'passed') { throw 'E_GA_REVIEW_PROVENANCE_REQUIRED' }
    # Exactly two independently reviewed source files; never bless a copy with
    # a newly computed expected hash. Generated configure payloads stay unchanged.
    & $State.v5 {param($p) New-OwnedDirectory $p} $State.paths.reviewed
    $State.directoryPins.Add('reviewed-copy',[pscustomobject]@{Path=$State.paths.reviewed;NativeIdentity=$State.context.task11Host.NativeIdentity($State.paths.reviewed)})
    $null=$State.entries.Add('D:outputs/configuration/reviewed')
    foreach($name in @('catalog','profile')) {
        Assert-GAInvariants $State
        $original=$State.pins['reviewed-source/'+$name];$State.readBytes=0L
        $source=Read-GAFile $State $original.Path 1048576 $true
        & $State.assertions {param($p,$o) Assert-GoObservedPin $p $o} $original $source
        $destination=[IO.Path]::Combine($State.paths.reviewed,$name+'.json')
        & $State.v5 {param($p,$b) Write-NewBytes $p $b} $destination $source.Data
        $null=$State.entries.Add('F:outputs/configuration/reviewed/'+$name+'.json')
        $copy=Read-GAFile $State $destination 1048576
        Add-GAPin $State ('reviewed-copy/'+$name) $copy $original.Bytes $original.Sha256
        Assert-GAInvariants $State
    }
}
function Read-GAPayloads($State,$Result,[string]$Directory,[string]$Operation) {
    $expected=if($Operation -ceq 'configure'){@('catalog','profile')}elseif($Operation -ceq 'save-config'){@('catalog','profile','template')}elseif($Operation -ceq 'sample-input'){@('data')}else{@('document')}
    $payloads=[ordered]@{}
    foreach($id in $expected) {
        $logical=switch($id){'catalog'{'catalog.json'}'profile'{'profile.json'}'template'{'template.pdf'}'data'{'data.json'}'document'{'document.pdf'}}
        $physical=if($id -ceq 'data'){$State.paths.sample}elseif($id -ceq 'document'){$Directory}else{[IO.Path]::Combine($Directory,$logical)}
        $capture=$id -cin @('catalog','profile','data')
        $value=Read-GAFile $State $physical $(if($capture){1048576}else{4194304}) $capture
        $value|Add-Member -NotePropertyName LogicalPath -NotePropertyValue $logical
        if($capture){$value|Add-Member -NotePropertyName Json -NotePropertyValue (Read-GAJson $State $value.Data 1048576)}
        $payloads.Add($id,$value)
    }
    return ,$payloads
}
function Confirm-GAConfiguration($State,$Result,[string]$Operation,[string]$Directory) {
    $payloads=Read-GAPayloads $State $Result $Directory $Operation
    $members=if($Operation -ceq 'configure'){@('catalog.json','profile.json','result.json','completion.json')}else{@('catalog.json','profile.json','template.pdf','result.json','completion.json')}
    $resultFile=Read-GAFile $State ([IO.Path]::Combine($Directory,'result.json')) 262144 $true
    $completionFile=Read-GAFile $State ([IO.Path]::Combine($Directory,'completion.json')) 262144 $true
    $committed=Read-GAJson $State $resultFile.Data;$completion=Read-GAJson $State $completionFile.Data
    Add-GADiagnosticReport $State ($Operation+'/committed-result') $resultFile.Data $true
    Add-GADiagnosticReport $State ($Operation+'/completion') $completionFile.Data $true
    # Invoke the existing public bounded completion reconciler, not a replacement
    # completion protocol. Both its assembly and the producer are exact pinned inputs.
    $expectedType=$State.context.publicationAssembly.GetType('PdfForm.Publication.ExpectedArtifact',$true)
    $array=[Array]::CreateInstance($expectedType,$payloads.Count);$index=0
    foreach($id in $payloads.Keys){$array.SetValue([Activator]::CreateInstance($expectedType,[object[]]@($id,$payloads[$id].LogicalPath)),$index);$index++}
    $limitsType=$State.context.publicationAssembly.GetType('PdfForm.Publication.PublicationLimits',$true)
    $limits=[Activator]::CreateInstance($limitsType,[object[]]@([long]4194304,[long]8388608,[int]3,[int]262144))
    Assert-GAClock $State
    $method=$State.context.publicationAssembly.GetType('PdfForm.Publication.CompletionProtocol',$true).GetMethod('Reconcile')
    $reconciled=$method.Invoke($null,[object[]]@($Directory,$completion.GetProperty('requestId').GetString(),$State.context.toolBuild,$array,$limits))
    Assert-GAClock $State
    if(-not $reconciled.IsComplete){throw 'E_GA_COMPLETION_REJECTED'}
    $State.directoryPins.Add($Operation,[pscustomobject]@{Path=$Directory;NativeIdentity=$State.context.task11Host.NativeIdentity($Directory)})
    foreach($receipt in @($Result,$committed)) {
        & $State.assertions {param($r,$op,$t,$p,$m) Assert-GoConfigurationReceipt $r 0 $op $t 'template.pdf' $p $m $true} $receipt $Operation $State.templatePin $payloads $members
        $binding=if($Operation -ceq 'configure'){'not_run'}else{'passed'}
        foreach($name in @('binding','bindingStructure')){if($receipt.GetProperty('validationLevels').GetProperty($name).GetString() -cne $binding){throw 'E_GA_CONFIGURATION_STAGE'}}
    }
    # Capture expected hashes from ORIGINAL validated descriptors before observing
    # any later command. No later read can redefine these expectations.
    foreach($output in $Result.GetProperty('outputs').EnumerateArray()) {
        $id=$output.GetProperty('id').GetString();$value=$payloads[$id]
        Add-GAPin $State ($Operation+'/'+$id) $value $output.GetProperty('bytes').GetInt64() $output.GetProperty('sha256').GetString()
    }
    Add-GAPin $State ($Operation+'/result') $resultFile $resultFile.Bytes $completion.GetProperty('result').GetProperty('sha256').GetString()
    Add-GAPin $State ($Operation+'/completion') $completionFile $completionFile.Bytes $completionFile.Sha256
    $relative=if($Operation -ceq 'configure'){'draft'}else{'saved'}
    $null=$State.entries.Add('D:outputs/configuration/'+$relative)
    foreach($member in $members){$null=$State.entries.Add('F:outputs/configuration/'+$relative+'/'+$member)}
}
function Assert-GANormalProcess($Process,[int]$ExpectedExit) {
    if(-not $Process.startConfirmed -or -not $Process.terminationObserved -or $Process.pending -or -not $Process.captureComplete -or
       $Process.timeout -or $Process.killAttempted -or $null -ne $Process.primaryCode -or $null -ne $Process.observationCode -or
       $null -ne $Process.waitCode -or $Process.secondaryCodes.Count -ne 0 -or $Process.exitCode -ne $ExpectedExit -or
       $Process.out.Length -gt 1048576 -or $Process.err.Length -ne 0){throw 'E_GA_PROCESS_OUTCOME'}
}
function Invoke-GAOwnedStep($State,$Slot,[int]$RemainingSlots) {
    Assert-GAClock $State $RemainingSlots
    if(@(& $State.v5 {Get-PendingPublicationProcess}).Count){throw 'E_PENDING_OWNED_PROCESS'}
    Assert-GAInvariants $State
    # The complete remaining reservation is checked again after fallible scans.
    Assert-GAClock $State $RemainingSlots
    $process=& $State.v5 {param($id) $p=New-ProcessState $id;$script:Custody.Add($p);return $p} $Slot.id
    $record=[pscustomobject]@{id=$Slot.id;operation=$Slot.operation;expectedExitCode=$Slot.exitCode;process=$process;passed=$false;code=$null}
    $State.records.Add($record)
    $clock=[Diagnostics.Stopwatch]::StartNew();$returned=$false
    try {
        & $State.v5 {param($p,$info,$c) Invoke-OwnedProcess $p $info $c} $process $Slot.startInfo $clock
        $returned=$true
        Add-GADiagnosticReport $State ($Slot.id+'/stdout') $process.out.ToArray() $true
        Add-GADiagnosticReport $State ($Slot.id+'/stderr') $process.err.ToArray() $false
        Assert-GANormalProcess $process $Slot.exitCode
        Assert-GAClock $State ($RemainingSlots-1)
        $State.readBytes=0L;$receipt=Read-GAJson $State $process.out.ToArray()
        if($Slot.id -cin @('go-sample-input','go-fill-editable','go-fill-flattened') -and $receipt.GetProperty('tool').GetProperty('runtime').GetString() -cne '.NET 10.0.13'){throw 'E_GA_REPORTED_RUNTIME'}
        switch($Slot.id) {
            'go-check' {
                & $State.assertions {param($r,$t) Assert-GoCompatibility $r 1 'configuration_needed' $t} $receipt $State.templatePin
                if($receipt.GetProperty('configurationState').GetString() -cne 'absent') { throw 'E_GA_CHECK_CONFIGURATION_STATE' }
            }
            'go-configure' {Confirm-GAConfiguration $State $receipt 'configure' $State.paths.draft}
            'go-save-config' {Confirm-GAConfiguration $State $receipt 'save-config' $State.paths.saved}
            'go-sample-input' {
                $payloads=Read-GAPayloads $State $receipt $State.paths.sample 'sample-input'
                # Join to the original validated save-config/profile pin, not the sample's identity claim.
                $profilePin=$State.pins['save-config/profile']
                $savedProfile=Read-GAFile $State $profilePin.Path 1048576 $true
                & $State.assertions {param($p,$o) Assert-GoObservedPin $p $o} $profilePin $savedProfile
                $profileJson=Read-GAJson $State $savedProfile.Data 1048576
                $profileIdentity=$profileJson.GetProperty('id').GetString()+'@'+$profileJson.GetProperty('revision').GetString()
                & $State.assertions {param($r,$t,$p,$identity) Assert-GoSampleReceipt $r 0 $t $p $identity} $receipt $State.templatePin $payloads $profileIdentity
                $descriptor=$receipt.GetProperty('outputs')[0]
                Add-GAPin $State 'generated-sample' $payloads['data'] $descriptor.GetProperty('bytes').GetInt64() $descriptor.GetProperty('sha256').GetString()
                $null=$State.entries.Add('F:outputs/sample-input.json')
                Add-GASamplePrivacyValues $State $payloads['data'].Json
            }
            {$_ -cin @('go-fill-editable','go-fill-flattened')} {
                $path=if($Slot.id -ceq 'go-fill-editable'){$State.paths.editable}else{$State.paths.flattened}
                $payloads=Read-GAPayloads $State $receipt $path 'fill'
                & $State.assertions {param($r,$t,$p) Assert-GoPublishedPdfReceipt $r 0 $t $p} $receipt $State.templatePin $payloads
                $descriptor=$receipt.GetProperty('outputs')[0]
                Add-GAPin $State $Slot.id $payloads['document'] $descriptor.GetProperty('bytes').GetInt64() $descriptor.GetProperty('sha256').GetString()
                $null=$State.entries.Add('F:outputs/'+[IO.Path]::GetFileName($path))
            }
            default {throw 'E_GA_FIXED_SLOT'}
        }
        # COMPLETE prior/copy/current set after every invocation, not only its input.
        Assert-GAInvariants $State
        Write-GAEvidence $State ($Slot.id+'.stdout.raw.bin') $process.out.ToArray()
        Write-GAEvidence $State ($Slot.id+'.stderr.raw.bin') $process.err.ToArray()
        $processReceipt=[ordered]@{id=$Slot.id;expectedExitCode=$Slot.exitCode;pid=$process.pid;processStartUtc=$process.processStartUtc
            startAttempted=$process.startAttempted;startOutcome=$process.startOutcome;terminationObserved=$process.terminationObserved;exitCode=$process.exitCode
            timeout=$process.timeout;killAttempted=$process.killAttempted;killOutcome=$process.killOutcome;postKillWait=$process.postKillWait
            captureComplete=$process.captureComplete;custodyRetained=$process.pending;primaryCode=$process.primaryCode;secondaryCodes=$process.secondaryCodes.ToArray()
            stdoutBytes=$process.out.Length;stderrBytes=$process.err.Length;batchElapsedMs=$State.clock.ElapsedMilliseconds}
        $processBytes=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $processReceipt -Depth 12 -Compress))
        Write-GAEvidence $State ($Slot.id+'.process.json') $processBytes
        Assert-GAInvariants $State
        $record.passed=$true
    } catch {
        $record.code=Get-GACode $_;$State.stopped=$true;$State.code=$record.code
        # Exactly the existing exceptional v5 settlement rule, never a second kill
        # after the supervisor has returned or already attempted termination.
        if(-not $returned -and $process.startAttempted -and -not $process.terminationObserved -and -not $process.killAttempted){
            try{& $State.v5 {param($p) Stop-ExactOwnedProcess $p} $process}catch{$process.secondaryCodes.Add('E_GA_EXCEPTIONAL_SETTLEMENT')}
        }
        if($process.startAttempted -and $process.startOutcome -cne 'returned-false' -and -not $process.terminationObserved){$process.pending=$true}
        # If custody settled, attempt full original-pin preservation even on failure.
        # Unknown new/staging entries intentionally make the domain check fail.
        if(-not $process.pending){try{Assert-GAInvariants $State}catch{$State.settlementCode=Get-GACode $_}}
    }
}
function Invoke-GenericOnboardingWorkflow {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$ReadOnlyAdmission)
    if($null -ne $script:GA){throw 'E_GA_SINGLE_ATTEMPT_ONLY'}
    $c=$ReadOnlyAdmission
    $s=[pscustomobject]@{ownerPid=$PID;runspaceId=[runspace]::DefaultRunspace.InstanceId;context=$c;clock=$c.admissionCompletedClock
        v5=$c.v5Module;assertions=$c.assertionsModule;paths=$null;freeze=$null;freezeJson=$null;freezePaths=$null;templatePin=$null
        pins=[ordered]@{};entries=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);readBytes=0L
        roots=[ordered]@{};directoryPins=[ordered]@{};records=[Collections.Generic.List[object]]::new();slots=@();admissionComplete=$false;stopped=$false;complete=$false;code=$null;settlementCode=$null;lastDomain=$null
        privacyTokens=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);privacyChars=0L;reports=[Collections.Generic.List[object]]::new();reportBytes=0L
        reviewGate='not_reached';reviewNeeded=$false}
    $script:GA=$s
    try {
        if($c.executionAuthorized -isnot [bool] -or -not $c.executionAuthorized -or $c.state -cne 'approved-fixed-generic-onboarding-plan'){throw 'E_GA_NOT_ADMITTED'}
        if($s.v5 -isnot [Management.Automation.PSModuleInfo] -or $s.assertions -isnot [Management.Automation.PSModuleInfo]){throw 'E_GA_MODULE_CONTEXT'}
        Assert-GAClock $s 6
        foreach($path in @($c.attemptRoot,$c.packetDirectory,$c.engineDirectory,$c.hostPath,$c.cliDll,$c.captureDirectory,$s.v5.Path,$s.assertions.Path,$PSScriptRoot)){
            & $s.v5 {param($p) Assert-AbsoluteLocalPath $p} $path
        }
        if($c.maximumRunBytes -lt 1 -or $c.maximumRunBytes -gt 2147483648 -or $c.reserveBytes -lt 12884901888 -or
           $c.memoryAdmission.ownerPid -ne $PID -or $c.memoryAdmission.inheritedPrivateBytesLimit -lt 1 -or $c.memoryAdmission.inheritedPrivateBytesLimit -gt 536870912 -or
           $c.memoryAdmission.nativeEnforcementObserved -isnot [bool] -or -not $c.memoryAdmission.nativeEnforcementObserved){throw 'E_GA_RESOURCE_ADMISSION'}
        # Memory admission is a root-owned live native envelope observation, not a
        # new job-object implementation or a JSON switch in this source adapter.
        if(-not @($c.domainOracle.GetType().GetInterfaces()).Where({$_.FullName -ceq 'PdfForm.FixedDriver.IFixedDomainOracleV1'}).Count -or
           -not @($c.task11Host.GetType().GetInterfaces()).Where({$_.FullName -ceq 'PdfForm.StandaloneThirdFormDriver.IExistingBoundedHarnessHost'}).Count){throw 'E_GA_EXISTING_HOST_TYPES'}
        $attempt=[IO.Path]::GetFullPath($c.attemptRoot)
        $p=[pscustomobject]@{bundle=$c.packetDirectory;cliDll=$c.cliDll;evidence=[IO.Path]::Combine($attempt,'evidence');state=[IO.Path]::Combine($attempt,'state')
            fixtures=[IO.Path]::Combine($attempt,'fixtures');configuration=[IO.Path]::Combine($attempt,'outputs','configuration')
            template=[IO.Path]::Combine($attempt,'fixtures','third-form.pdf');reviewedSource=[IO.Path]::Combine($attempt,'fixtures','reviewed-draft')
            draft=[IO.Path]::Combine($attempt,'outputs','configuration','draft');reviewed=[IO.Path]::Combine($attempt,'outputs','configuration','reviewed')
            saved=[IO.Path]::Combine($attempt,'outputs','configuration','saved');catalog=[IO.Path]::Combine($attempt,'outputs','configuration','saved','catalog.json')
            sample=[IO.Path]::Combine($attempt,'outputs','sample-input.json');editable=[IO.Path]::Combine($attempt,'outputs','editable.pdf');flattened=[IO.Path]::Combine($attempt,'outputs','flattened.pdf')}
        $s.paths=$p
        if($c.packetDirectory -cne [IO.Path]::Combine($attempt,'packet') -or $c.captureDirectory -cne $p.evidence -or -not $c.engineDirectory.StartsWith($c.packetDirectory+'\',[StringComparison]::OrdinalIgnoreCase) -or
           -not $c.cliDll.StartsWith($c.engineDirectory+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'E_GA_FIXED_PATH_ROLES'}
        foreach($path in $p.PSObject.Properties.Value){& $s.v5 {param($x) Assert-AbsoluteLocalPath $x} $path}
        Add-GAFixturePrivacyTokens $s
        foreach($path in @($c.attemptRoot,$c.engineDirectory,$c.hostPath,$s.v5.Path,$s.assertions.Path)+@($p.PSObject.Properties.Value)) { Add-GAPrivacyToken $s $path }
        if($null -ne $c.PSObject.Properties['diagnosticCanaries']) { foreach($token in @($c.diagnosticCanaries)) { Add-GAPrivacyToken $s $token } }
        # Validate the entire finite plan before the first metadata-dependent
        # setup write, not after a destination has already been created.
        $plan=New-GenericOnboardingFixedPlan $p $c.runtimeVersion
        Assert-GenericOnboardingFixedPlan $c.plan $p $c.runtimeVersion
        $s.readBytes=0L
        foreach($pin in $c.originalPins) {
            if($pin.Key -isnot [string] -or $pin.Sha256 -cnotmatch '\A[0-9a-f]{64}\z'){throw 'E_GA_ORIGINAL_PIN'}
            $actual=Read-GAFile $s $pin.Path 67108864
            & $s.assertions {param($pin,$a) Assert-GoObservedPin $pin $a} $pin $actual
            Add-GAPin $s $pin.Key $actual $pin.Bytes $pin.Sha256
        }
        # Every loaded component/host input is joined to an original independent pin.
        foreach($spec in @(@('v5',$s.v5.Path,$script:V5Hash),@('assertions',$s.assertions.Path,$c.assertionsSha256),
            @('adapter',$PSCommandPath,$c.adapterSha256),@('oracle',$c.domainOracle.GetType().Assembly.Location,$c.oracleAssemblySha256),
            @('task11-host',$c.task11Host.GetType().Assembly.Location,$c.task11HostAssemblySha256),@('publication',$c.publicationAssembly.Location,$c.publicationAssemblySha256),
            @('dotnet',$c.hostPath,$c.hostSha256),@('cli',$c.cliDll,$c.cliSha256),@('entry-script',[IO.Path]::Combine($PSScriptRoot,'Test-GenericOnboarding.ps1'),$c.entryScriptSha256))) {
            if(-not $s.pins.Contains($spec[0]) -or $s.pins[$spec[0]].Path -cne $spec[1] -or $s.pins[$spec[0]].Sha256 -cne $spec[2]){throw 'E_GA_COMPONENT_PIN_JOIN'}
        }
        foreach($key in @('template','freeze-evidence','introduction-evidence','entry-script')){if(-not $s.pins.Contains($key)){throw 'E_GA_REQUIRED_PIN'}}
        if($s.pins['template'].Path -cne $p.template){throw 'E_GA_FIXTURE_PIN_JOIN'}
        foreach($name in @('catalog','profile')) {
            $key='reviewed-source/'+$name
            if($s.pins.Contains('reviewed-draft-provenance') -and -not $s.pins.Contains($key)){throw 'E_GA_REQUIRED_PIN'}
            if($s.pins.Contains($key) -and $s.pins[$key].Path -cne [IO.Path]::Combine($p.reviewedSource,$name+'.json')){throw 'E_GA_FIXTURE_PIN_JOIN'}
        }
        $s.templatePin=$s.pins['template']
        if($s.pins.Contains('reviewed-source/catalog') -or $s.pins.Contains('reviewed-source/profile')) {
            $s.directoryPins.Add('reviewed-source',[pscustomobject]@{Path=$p.reviewedSource;NativeIdentity=$c.task11Host.NativeIdentity($p.reviewedSource)})
        }
        $freezeFile=Read-GAFile $s $s.pins['freeze-evidence'].Path 262144 $true
        $introFile=Read-GAFile $s $s.pins['introduction-evidence'].Path 262144 $true
        $s.freezeJson=Read-GAJson $s $freezeFile.Data
        $s.freeze=& $s.v5 {param($b) Read-StrictJson $b} $freezeFile.Data
        $intro=Read-GAJson $s $introFile.Data
        if($intro.GetProperty('firstFixtureSequence').GetInt64() -ne $c.firstFixtureSequence -or
           $intro.GetProperty('freeze').GetProperty('sequence').GetInt64() -ne $s.freeze.sequence -or
           $intro.GetProperty('freeze').GetProperty('launcherSha256').GetString() -cne $s.freeze.launcherSha256){throw 'E_GA_TASK11_EVIDENCE_JOIN'}
        & $s.assertions {param($f,$intro,$n) Assert-GoFrozenClosure $f $intro $n} $s.freezeJson $intro.GetProperty('freeze') $c.firstFixtureSequence
        $s.freezePaths=$c.freezePaths
        if($s.freeze.files.Count -ne $s.freezePaths.Count){throw 'E_GA_FREEZE_PATH_MAP'}
        foreach($file in $s.freeze.files){
            $expected=if($file.relativePath -ceq 'engine/.'){$c.engineDirectory}elseif($file.relativePath.StartsWith('engine/',[StringComparison]::Ordinal)){
                $relative=$file.relativePath.Substring(7);& $s.v5 {param($r) Assert-RelativePath $r} $relative;[IO.Path]::Combine($c.engineDirectory,$relative.Replace('/','\'))
            }elseif($file.relativePath -cmatch '\Aruntime/([0-9]{3})\z'){$c.externalRuntimeFiles[[int]$Matches[1]]}else{throw 'E_GA_FREEZE_MEMBER'}
            if($s.freezePaths[$file.relativePath] -cne $expected){throw 'E_GA_FREEZE_PATH_MAP'}
        }
        # Frozen launcher must be the actual exact host used by all six fresh children.
        if($s.freeze.launcherSha256 -cne $c.hostSha256){throw 'E_GA_FROZEN_LAUNCHER'}
        # Complete read-only admission and the original empty-output observation
        # before any adapter setup write. Input/fixture introduction already used
        # task11's independent sequence evidence outside the immutable packet.
        Assert-GAInvariants $s
        $s.admissionComplete=$true
        $roots=[ordered]@{}
        foreach($name in @('bundle','fixtures','configuration','evidence')) {
            $path=$p.$name
            if($name -ceq 'configuration'){
                & $s.v5 {param($x) New-OwnedDirectory $x} $path
                $null=$s.entries.Add('D:outputs/configuration')
            }
            $roots.Add($name,[pscustomobject]@{Path=$path;NativeIdentity=$c.task11Host.NativeIdentity($path)})
        }
        & $s.assertions {param($r) Assert-GoDisjointRoots $r} $roots
        $s.roots=$roots
        Assert-GAInvariants $s
        $slots=[Collections.Generic.List[object]]::new()
        for($i=0;$i -lt 6;$i++) {
            Assert-GAClock $s 6
            $slot=$plan[$i];$isolation=[IO.Path]::Combine($p.state,$slot.id)
            & $s.v5 {param($x) New-OwnedDirectory $x} $isolation
            $info=& $s.v5 {param($a,$packet,$fixtures,$isolation) New-StartInfo $a $packet $fixtures $isolation} $c.publicationAdmission $p.bundle $p.fixtures $isolation
            $info.ArgumentList.Clear();foreach($argument in $slot.argv){$info.ArgumentList.Add($argument)}
            if($info.FileName -cne $c.hostPath -or $info.WorkingDirectory -cne $p.bundle -or $info.UseShellExecute -or -not $info.CreateNoWindow -or
               -not $info.RedirectStandardOutput -or -not $info.RedirectStandardError -or -not [string]::IsNullOrEmpty($info.Arguments)){throw 'E_GA_STARTINFO'}
            $slot|Add-Member -NotePropertyName startInfo -NotePropertyValue $info;$slots.Add($slot)
        }
        $s.slots=$slots.ToArray()
        for($i=0;$i -lt 6;$i++) {
            if($i -eq 2){Assert-GAClock $s 4;if(-not (Confirm-GAReviewedProvenance $s)){break};Copy-GAReviewed $s}
            Invoke-GAOwnedStep $s $s.slots[$i] (6-$i)
            if($s.stopped){break}
        }
        $s.complete=-not $s.stopped -and $s.records.Count -eq 6 -and @($s.records).Where({-not $_.passed}).Count -eq 0
    } catch {$s.stopped=$true;$s.complete=$false;$s.code=Get-GACode $_}
    $pending=@($s.records).Where({$_.process.pending}).Count
    if($s.admissionComplete -and $pending -eq 0 -and $null -ne $s.paths -and $null -ne $s.freeze) {
        try {
            $status=if($s.reviewNeeded){'configuration_needed'}elseif($s.complete){'ok'}elseif($s.reviewGate -ceq 'failed'){'input_error'}else{'internal_error'}
            $exit=if($s.reviewNeeded){1}elseif($s.complete){0}elseif($s.reviewGate -ceq 'failed'){2}else{4}
            $terminal=[ordered]@{schema='pdfform.generic-onboarding-adapter/1';complete=$s.complete;status=$status;exitCode=$exit;reviewGate=$s.reviewGate;reviewNeeded=$s.reviewNeeded;acceptance=$false;requires='matching-console-final-domain-observation';code=$s.code;selected=$script:GAIds;executed=@($s.records|ForEach-Object{$_.id});manualReview='required';visualAcceptance='not_observed';perFieldWidgetVerification='not_observed';editableSaveReopen='not_observed'}
            $bytes=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $terminal -Depth 12 -Compress))
            Write-GAEvidence $s 'generic-onboarding.terminal.json' $bytes
            # No filesystem write follows the complete final domain/pin observation.
            Assert-GAInvariants $s
        } catch {$s.stopped=$true;$s.complete=$false;$s.settlementCode=Get-GACode $_}
    } else {$s.stopped=$true;$s.complete=$false}
    $status=if($pending -ne 0 -or $null -ne $s.settlementCode){'internal_error'}elseif($s.reviewNeeded){'configuration_needed'}elseif($s.complete){'ok'}elseif($s.reviewGate -ceq 'failed'){'input_error'}else{'internal_error'}
    $exit=if($pending -ne 0 -or $null -ne $s.settlementCode){4}elseif($s.reviewNeeded){1}elseif($s.complete){0}elseif($s.reviewGate -ceq 'failed'){2}else{4}
    return [pscustomobject]@{schema='pdfform.generic-onboarding-console/1';complete=$s.complete;status=$status;exitCode=$exit;reviewGate=$s.reviewGate;reviewNeeded=$s.reviewNeeded;code=$s.code;settlementCode=$s.settlementCode;pendingOwnedProcesses=$pending
        selected=$script:GAIds;executed=@($s.records|ForEach-Object{$_.id});failed=@($s.records|Where-Object{-not $_.passed}|ForEach-Object{$_.id})
        skipped=@($script:GAIds|Where-Object{$id=$_;@($s.records).Where({$_.id -ceq $id}).Count -eq 0})
        finalDomain=$s.lastDomain;stateRetained=$true;acceptance=$false;visualAcceptance='not_observed';perFieldWidgetVerification='not_observed';editableSaveReopen='not_observed'}
}
Export-ModuleMember -Function Invoke-GenericOnboardingWorkflow,Get-GAState,New-GenericOnboardingFixedPlan,Assert-GenericOnboardingFixedPlan
