# Curated overrides

Hand-curated corrections that win over mined data. One `*.json` file per game (Steam id is the key).
The miner applies these as the final merge step (`--with-overrides`), after the Ludusavi backbone +
MO2 enrichment. Curated data wins over everything the miner produced.

## Format (camelCase, all fields except `steamAppId` optional)

```json
{
  "steamAppId": "72850",
  "id": "skyrim",
  "name": "The Elder Scrolls V: Skyrim",
  "engine": "bethesda",
  "modPath": "Data",
  "nexusDomain": "skyrim",
  "featured": 20,
  "fileExtensions": ["esp", "esl", "esm", "bsa"]
}
```

`engine` must be a real engine key (`bethesda`, `ue-pak`, `bepinex`, `smapi`, `minecraft`, `source`,
`melonloader`, `fromsoft`, `custom`). An override whose `steamAppId` isn't in the backbone ADDS a new
entry; one that matches OVERRIDES the mined fields. Unspecified fields are left as the miner set them.

To add a game: drop a `<game>.json` here, run `dotnet run --project tools/ManifestMiner -- --with-mo2
--with-overrides`, and check the coverage summary + the diff. Verify the Steam id (a wrong id just
won't match — it's reported as not-applied, never corrupts).
