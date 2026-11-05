# Compatibility

`lip` MUST install the git archive at the highest version tag (Section 2.1 of [PACKAGE]) of each remote repository below, with every variant in every environment that variant applies to, and with its dependencies, as its `tooth.json` [TOOTH-V3] specifies:

- `github.com/LiteLDev/LeviLamina`
- `github.com/LiteLDev/LegacyScriptEngine`
- `github.com/LiteLDev/LeviAntiCheat`

## Security Considerations

Installing these runs their `scripts` and downloads their assets unverified, trusting their repositories and asset hosts.
