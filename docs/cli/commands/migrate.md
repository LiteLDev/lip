# lip migrate

Migrate the package repository in the current directory from `tooth.json` to `manifest.json`.

## Synopsis

```shell
lip migrate
```

## Description

Run it in the root directory of your package repository, the one that contains `tooth.json` (format version 1, 2 or 3). It:

1. converts `tooth.json` to a format 4 `manifest.json`, as far as the formats allow:
    - A `tooth.json` with a single unlabeled variant whose assets are all `self` becomes a file package (`"type": "files"`).
    - Any other `tooth.json` becomes a routing document with one artifact per variant (an unlabeled and a `client` variant become server and client artifacts). You still have to publish the concrete package of each variant.
2. writes `manifest.json`. If the repository already has one (for example the LeviLamina manifest of your mod), the converted keys are merged into it: keys it does not have yet, including the format markers, are appended in its own indentation; keys it already has are never overwritten, and lip warns for each one whose value differs from the converted value. Everything else in the file is left as it is.
3. deletes `tooth.json`, only once `manifest.json` has been written.

If there is no `tooth.json` in the current directory, or `tooth.json` or the existing `manifest.json` cannot be read, `lip migrate` fails and changes nothing.

Everything that cannot be expressed in `manifest.json`, or needs a decision, is printed as a warning naming the `tooth.json` field (in its format 3 form). Warnings do not make the command fail. The output is a starting point, not a guarantee: review it before publishing, and commit the result. See [Migrating Packages](../../guides/migrating_packages.md) for the details and limitations.
