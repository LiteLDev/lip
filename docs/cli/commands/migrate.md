# lip migrate

Convert a `tooth.json` to a `manifest.json`.

## Synopsis

```shell
lip migrate <FILE> <OUTPUT>
```

## Arguments

| Argument | Description |
| --- | --- |
| `<FILE>` | The `tooth.json` to convert (format version 1, 2 or 3). |
| `<OUTPUT>` | The path of the `manifest.json` to write. |

## Description

Reads a `tooth.json` and writes an equivalent format 4 `manifest.json`, as far as the formats allow:

- A `tooth.json` with a single unlabeled variant whose assets are all `self` becomes a file package (`"type": "files"`).
- Any other `tooth.json` becomes a routing document with one artifact per variant (an unlabeled and a `client` variant become server and client artifacts). You still have to publish the concrete package of each variant.

Everything that cannot be converted, or needs a decision, is printed as a warning. The output is a starting point, not a guarantee: review it before publishing. See [Migrating Packages](../../guides/migrating_packages.md) for the details and limitations.
