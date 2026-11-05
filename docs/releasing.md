# Releasing Naad

A release is made by pushing a version tag, such as `v2.2.0`, on a commit
that is already on `main`. `.github/workflows/release.yml` then:

1. checks that the tagged commit is on `main`;
2. runs the tests, publishes `Naad.exe` and packs the installer with
   Velopack (`vpk pack`);
3. writes the **release list**, `naad-release.json`, and signs it;
4. uploads the release as a draft, attaches the list and its signature,
   and only then publishes it.

`CHANGELOG.md` must have a section for the version, which becomes the
release notes.

## The signed release list

Installed copies of Naad take an update only from a release made with
Naad's signing key. The release list names every file of the release with
its size and SHA-256, the version, and when it was signed:

```json
{"packId":"Naad.App","version":"2.2.0","published":"2026-11-05T12:00:00.0000000+00:00",
 "files":[{"name":"Naad.App-2.2.0-full.nupkg","size":61234567,"sha256":"…"}, …]}
```

`naad-release.json.sig` holds the signature of those exact bytes: ECDSA
P-256 with SHA-256, the 64-byte r‖s value in base64.

Naad builds in the public key (`PublicKeys` in
`src/Naad/Updates/AppUpdater.cs`). When a check finds a release, Naad
downloads the list and its signature from the same release and:

- refuses the release unless the signature checks out with one of its
  keys and the list names the package Velopack found, with the same
  version, size and SHA-256;
- counts the three-day wait from the list's `published` time;
- after downloading, checks the package against the list again, byte for
  byte, and deletes it instead of installing it if it differs.

A release without a list, or with a list signed by another key, is never
offered. Copies older than 2.2.0 have no key and keep updating as before
until they install 2.2.0.

## Making the key pair

Do this once, on your own PC, never in CI. With OpenSSL:

```sh
openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt -out naad-update-signing-key.pem
openssl ec -in naad-update-signing-key.pem -pubout
```

Or with PowerShell 7:

```powershell
$key = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
$key.ExportPkcs8PrivateKeyPem() | Set-Content naad-update-signing-key.pem
$key.ExportSubjectPublicKeyInfoPem()
```

The second command prints the public key (`-----BEGIN PUBLIC KEY-----`).
That goes into `PublicKeys` in `AppUpdater.cs`.

## Keeping the private key out of the repository

- Never commit `naad-update-signing-key.pem`, and never paste it into an
  issue, a log or a chat.
- Add it as a repository secret named `NAAD_UPDATE_SIGNING_KEY`: in GitHub,
  **Settings > Secrets and variables > Actions > New repository secret**,
  with the whole file, including the `BEGIN` and `END` lines, as the value.
  Or: `gh secret set NAAD_UPDATE_SIGNING_KEY < naad-update-signing-key.pem`.
- Keep an offline backup (a password manager or an encrypted drive). A lost
  key means rotating, and copies that only know the lost key's public half
  can't take another update.
- Delete the file from the PC once it is stored and backed up.

The release workflow fails at "Sign the release list" when the secret is
missing, so no release goes out unsigned.

## Rotating the key

When the key may have leaked, or to replace it:

1. Make a new pair as above.
2. Add the new public key to `PublicKeys`, beside the old one, and release
   that version **signed with the old key**. Copies that install it trust
   both keys.
3. Replace the `NAAD_UPDATE_SIGNING_KEY` secret with the new private key.
4. After most copies have updated (allow a few weeks), remove the old
   public key from `PublicKeys` in a later release, signed with the new key.

If the old key leaked, skip waiting in step 4: release the version with
only the new key as soon as step 2's release is out. A copy that never
installed step 2's release must then be updated by running the new
installer from the releases page.

## Checking a release

After the workflow finishes, check that the release has
`naad-release.json` and `naad-release.json.sig` beside the installer, then
run Check for updates on an installed older copy (M-UP3 in
[manual-tests.md](manual-tests.md)).
