# lip migrate

Migrate the package repository in the current directory from `tooth.json` or a LeviLamina manifest to `manifest.json`.

## Synopsis

```shell
lip migrate
```

## Description

Run it in the root directory of your package repository. It writes a `manifest.json` with `"format": "bf45b1"`, indented with two spaces:

- **From `tooth.json`** (format version 1, 2 or 3), when the directory contains one:
    - A `tooth.json` with a single unlabeled variant whose assets are all `self` and whose `platform` is not a pattern becomes a file package; its `platform` becomes `runtime`.
    - Any other `tooth.json` becomes a meta document whose `targets` (an unlabeled and a `client` variant become server and client targets) each point to a `.mcmod` file to publish. The package document of the target with index `<i>` is written to `targets/<i>/manifest.json`; you build the `.mcmod` file from it with [`lip pack`](pack.md). If `targets` already exists, the command fails and changes nothing.
    - The `tooth.json` must be the only manifest: if `manifest.json` also exists, the command fails and changes nothing. `tooth.json` is deleted, only once `manifest.json` has been written.
- **From a LeviLamina manifest**, otherwise: the `manifest.json` without `format` is rewritten in place with `"format": "bf45b1"` added. Its other keys already have the shape lip reads, and are kept, except a `platform` other than `"server"` or `"client"`, and entries of `dependencies` and `conflicts` whose `name` is not an id, which are dropped; a `version` in them that is not a node-semver range is removed.

When a value the result needs is missing or invalid (for example a `version` that is a build placeholder, or the RIDs a `tooth.json` platform pattern stands for), lip asks for it on the terminal until the answer is valid. When standard input or standard error is not a terminal, it fails instead, naming the value it would ask for, and changes nothing.

If neither `tooth.json` nor `manifest.json` exists, `manifest.json` already has the `format` key, `targets` exists and the result is a meta document, or a file cannot be read, `lip migrate` fails and changes nothing.

Everything that is dropped or converted with another meaning is printed as a warning naming the field it comes from (for `tooth.json`, in its format 3 form). Warnings do not make the command fail. The output is a starting point, not a guarantee: review it before publishing, and commit the result. See [Migrating Packages](../../guides/migrating_packages.md) for the details and limitations.
