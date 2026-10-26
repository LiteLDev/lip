# Migrating Packages

As lip evolves, the format of `tooth.json` may change. The `lip migrate` command helps you upgrade your manifest files to the latest version.

## Using `lip migrate`

To migrate an old `tooth.json` file to the current format version (v3), use the `migrate` command.

```sh
lip migrate <FILE> <OUTPUT>
```

-   `<FILE>`: The path to your existing `tooth.json` (or similarly named file) that is in an older format.
-   `<OUTPUT>`: The path where the new, migrated manifest is written.

### Example

Suppose you have a v2 manifest named `tooth.old.json`.

```sh
lip migrate tooth.old.json tooth.json
```

This will read `tooth.old.json`, apply the necessary transformations to upgrade it to v3, and save the result to `tooth.json`.

## Migration Logic

lip automatically handles:
-   Updating `format_version`.
-   Restructuring object fields that have moved or been renamed.
-   Adding default values for new required fields.

Always verify the output `tooth.json` to ensure the migration preserved your configuration as expected.
