# SOURCE ONLY / NOT_RUN. Root loads the pinned assertion fixtures and exact
# assertion/workflow modules, then registers these pure controls in its existing
# bounded test host. No import, registration or execution occurs on load.
function New-GoR3ProvenanceFixture {
    $template=[pscustomobject]@{Bytes=128L;Sha256=('c'*64)}
    $draft=[ordered]@{catalog=[pscustomobject]@{Bytes=5L;Sha256=('a'*64)};profile=[pscustomobject]@{Bytes=64L;Sha256=('b'*64)}}
    $reviewed=[ordered]@{catalog=[pscustomobject]@{Bytes=7L;Sha256=('d'*64)};profile=[pscustomobject]@{Bytes=96L;Sha256=('e'*64)}}
    $proof=[ordered]@{schema='pdfform.reviewed-draft-provenance/1';producerCliSha256=('f'*64);template=[ordered]@{path='template.pdf';bytes=128L;sha256=$template.Sha256}
        sourceDraft=[ordered]@{catalog=[ordered]@{path='catalog.json';bytes=5L;sha256=('a'*64)};profile=[ordered]@{path='profile.json';bytes=64L;sha256=('b'*64)}}
        reviewedCopy=[ordered]@{catalog=[ordered]@{path='catalog.json';bytes=7L;sha256=('d'*64)};profile=[ordered]@{path='profile.json';bytes=96L;sha256=('e'*64)}}
        reviewReference='approved-synthetic-edits-from-source-cut'}
    return [pscustomobject]@{Proof=$proof;Template=$template;Draft=$draft;Reviewed=$reviewed;CliSha256=('f'*64)}
}
function New-GoR3PrivacyState($AssertionsModule) {
    return [pscustomobject]@{clock=[Diagnostics.Stopwatch]::StartNew();assertions=$AssertionsModule
        privacyTokens=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);privacyChars=0L
        reports=[Collections.Generic.List[object]]::new();reportBytes=0L}
}
function Register-GenericOnboardingR3ReviewCases([scriptblock]$Register,$WorkflowModule,$AssertionsModule) {
    $envelopeMutations=@(
        @('tool-old-sourceRevision',{param($f)$f.Receipt.tool.Remove('sourceReference');$f.Receipt.tool.Add('sourceRevision','fixture')},'E_GO_OBJECT_MEMBERS'),
        @('tool-nested-value',{param($f)$f.Receipt.tool.Add('values',[ordered]@{participant='FICTIONAL MAPLE MAKER'})},'E_GO_OBJECT_MEMBERS'),
        @('tool-object-in-scalar',{param($f)$f.Receipt.tool.runtime=[ordered]@{value='FICTIONAL MAPLE MAKER'}},'E_GO_STRING'),
        @('profiles-unexpected',{param($f)$f.Receipt.profiles=@([ordered]@{value='FICTIONAL MAPLE MAKER'})},'E_GO_ARRAY'),
        @('stats-nested-value',{param($f)$f.Receipt.stats.Add('values',[ordered]@{participant='FICTIONAL MAPLE MAKER'})},'E_GO_OBJECT_MEMBERS'),
        @('warnings-not-array',{param($f)$f.Receipt.warnings=[ordered]@{value='FICTIONAL MAPLE MAKER'}},'E_GO_ARRAY'),
        @('warning-extra-nested-value',{param($f)$f.Receipt.warnings=@([ordered]@{code='W_CONFIGURATION_ASSESSMENT';stage='binding';message='Review the indicated compatibility limitation.';severity='warning';values=[ordered]@{participant='FICTIONAL MAPLE MAKER'}})},'E_GO_OBJECT_MEMBERS'),
        @('warning-leaked-message',{param($f)$f.Receipt.warnings=@([ordered]@{code='W_CONFIGURATION_ASSESSMENT';stage='binding';message='FICTIONAL MAPLE MAKER';severity='warning'})},'E_GO_TEXT_MISMATCH'),
        @('warning-private-key',{param($f)$f.Receipt.warnings=@([ordered]@{code='W_CONFIGURATION_ASSESSMENT';stage='binding';message='Review the indicated compatibility limitation.';severity='warning';key='PRIVATE_SEMANTIC_KEY'})},'E_GO_COMMAND_WARNING'),
        @('unexpected-request-value',{param($f)$f.Receipt.Add('requestId','FICTIONAL MAPLE MAKER')},'E_GO_OBJECT_MEMBERS')
    )
    foreach($operation in @('fill','sample-input')) {
        foreach($case in $envelopeMutations) {
            $op=$operation;$mutate=$case[1];$code=$case[2]
            $body={
                $f=New-GoCommandFixture $op;& $mutate $f
                Require-GoCaseRejected {
                    if($op -ceq 'fill'){Assert-GoPublishedPdfReceipt (Convert-GoCaseElement $f.Receipt) 0 $f.Template $f.Payloads}
                    else{Assert-GoSampleReceipt (Convert-GoCaseElement $f.Receipt) 0 $f.Template $f.Payloads $f.SavedProfileIdentity}
                } $code
            }.GetNewClosure()
            & $Register ('r3-envelope-'+$operation+'-'+$case[0]) $body
        }
    }
    foreach($stage in @('syntax','profile','data','binding','bindingStructure','render','packet')) {
        $name=$stage
        $body={
            $f=New-GoCommandFixture 'fill';$f.Receipt.validationLevels[$name]=if($name -ceq 'packet'){'passed'}else{'not_run'}
            Require-GoCaseRejected {Assert-GoPublishedPdfReceipt (Convert-GoCaseElement $f.Receipt) 0 $f.Template $f.Payloads} 'E_GO_VALIDATION_LEVEL'
        }.GetNewClosure()
        & $Register ('r3-fill-level-'+$stage) $body
    }
    & $Register 'r3-sample-cannot-claim-render' {
        $f=New-GoCommandFixture 'sample-input';$f.Receipt.validationLevels.render='passed'
        Require-GoCaseRejected {Assert-GoSampleReceipt (Convert-GoCaseElement $f.Receipt) 0 $f.Template $f.Payloads $f.SavedProfileIdentity} 'E_GO_VALIDATION_LEVEL'
    }
    & $Register 'r3-intentional-sample-export-is-not-screened-as-a-report' {
        $f=New-GoCommandFixture 'sample-input'
        Assert-GoSampleReceipt (Convert-GoCaseElement $f.Receipt) 0 $f.Template $f.Payloads $f.SavedProfileIdentity
        $report=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $f.Receipt -Depth 24 -Compress))
        Assert-GoDiagnosticReport $report $true @('FICTIONAL MAPLE MAKER')
    }
    $diagnostics=@(
        @('raw-canary','{"warning":"PRIVATE_SENTINEL"}','E_GO_DIAGNOSTIC_VALUE'),
        @('decoded-nested-value','{"metadata":{"values":{"participant":"F\u0049CTIONAL MAPLE MAKER"}}}','E_GO_DIAGNOSTIC_VALUE'),
        @('decoded-warning','{"warnings":[{"message":"PR\u0049VATE_SENTINEL"}]}','E_GO_DIAGNOSTIC_VALUE'),
        @('decoded-private-path','{"metadata":{"path":"\u0043:\u005cUsers\u005cPrivate\u005cinput.pdf"}}','E_GO_DIAGNOSTIC_PATH')
    )
    foreach($case in $diagnostics) {
        $wire=$case[1];$code=$case[2]
        $body={Require-GoCaseRejected {Assert-GoDiagnosticReport ([Text.UTF8Encoding]::new($false).GetBytes($wire)) $true @('PRIVATE_SENTINEL','FICTIONAL MAPLE MAKER')} $code}.GetNewClosure()
        & $Register ('r3-report-'+$case[0]) $body
    }
    & $Register 'r3-recheck-earlier-report-after-sample-value-discovery' {
        $s=New-GoR3PrivacyState $AssertionsModule
        $raw=[Text.UTF8Encoding]::new($false).GetBytes('{"capability":"LATE GENERATED PARTICIPANT"}')
        & $WorkflowModule {param($s,$b) Add-GADiagnosticReport $s 'earlier-report' $b $true} $s $raw
        $data=Convert-GoCaseElement ([ordered]@{values=[ordered]@{participant='LATE GENERATED PARTICIPANT'}})
        Require-GoCaseRejected {& $WorkflowModule {param($s,$d) Add-GASamplePrivacyValues $s $d} $s $data} 'E_GO_DIAGNOSTIC_VALUE'
    }.GetNewClosure()
    & $Register 'r3-sample-learning-does-not-submit-export-to-report-screen' {
        $s=New-GoR3PrivacyState $AssertionsModule
        $data=Convert-GoCaseElement ([ordered]@{values=[ordered]@{participant='FICTIONAL MAPLE MAKER'}})
        & $WorkflowModule {param($s,$d) Add-GASamplePrivacyValues $s $d} $s $data
        if($s.reports.Count -ne 0 -or -not $s.privacyTokens.Contains('FICTIONAL MAPLE MAKER')){throw 'E_GO_R3_EXPORT_SCOPE'}
    }.GetNewClosure()
    & $Register 'r3-provenance-permits-documented-edited-bytes' {
        $f=New-GoR3ProvenanceFixture
        Assert-GoReviewedDraftProvenance (Convert-GoCaseElement $f.Proof) $f.Template $f.CliSha256 $f.Draft $f.Reviewed
        if($f.Draft.profile.Sha256 -ceq $f.Reviewed.profile.Sha256){throw 'E_GO_R3_EDIT_CONTROL'}
    }
    foreach($set in @('sourceDraft','reviewedCopy')) {
        foreach($kind in @('catalog','profile')) {
            $which=$set;$id=$kind
            $body={
                $f=New-GoR3ProvenanceFixture;$f.Proof[$which][$id].sha256=('0'*64)
                Require-GoCaseRejected {Assert-GoReviewedDraftProvenance (Convert-GoCaseElement $f.Proof) $f.Template $f.CliSha256 $f.Draft $f.Reviewed} 'E_GO_DRAFT_PROVENANCE'
            }.GetNewClosure()
            & $Register ('r3-provenance-mismatch-'+$set+'-'+$kind) $body
        }
    }
}
