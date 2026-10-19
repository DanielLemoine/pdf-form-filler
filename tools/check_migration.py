"""Reproduce public migration in an owned temporary tree without changing checked-in artifacts."""
import json
import pathlib
import shutil
import subprocess
import sys
import tempfile

root = pathlib.Path(__file__).resolve().parents[1]
scratch = root / '.runtime' / 'migration-check'
scratch.mkdir(parents=True, exist_ok=True)
with tempfile.TemporaryDirectory(prefix='attempt-', dir=scratch) as temporary:
    target = pathlib.Path(temporary)
    for relative in ('tools', 'profiles', 'samples', 'tests/PdfForm.Core.Tests'):
        (target / relative).mkdir(parents=True)
    shutil.copytree(root / 'fixtures/contracts', target / 'fixtures/contracts')
    shutil.copyfile(root / 'tools/migrate_reviewed_inputs.py', target / 'tools/migrate_reviewed_inputs.py')
    subprocess.run([sys.executable, str(target / 'tools/migrate_reviewed_inputs.py')],
                   cwd=target, check=True, timeout=10)
    count = 0
    for relative in ('profiles', 'samples', 'tests/PdfForm.Core.Tests'):
        for generated in sorted((target / relative).glob('*.json')):
            expected = root / generated.relative_to(target)
            if generated.read_bytes() != expected.read_bytes():
                raise AssertionError('Reproduction mismatch: ' + str(generated.relative_to(target)))
            count += 1
    manifest = pathlib.Path('fixtures/contracts/profile-migration-manifest.json')
    if json.loads((target / manifest).read_bytes()) != json.loads((root / manifest).read_bytes()):
        raise AssertionError('Migration manifest mismatch')
    print(f'PASS deterministic migration: {count} exact artifact byte comparisons and manifest facts')
