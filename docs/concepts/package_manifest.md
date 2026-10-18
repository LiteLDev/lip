# Package Manifest

The package manifest file `tooth.json` defines a lip package.

> [!TIP]
> <span v-pre>You can use variables from other fields in any string field by wrapping the variable name in `{{}}` like `{{version}}` and `{{info.name}}`. For example, `{{version}}` will be replaced with the value of the `version` field.</span>

## Example

```json
{
  "format_version": 3,
  "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
  "tooth": "github.com/user/repo",
  "version": "1.0.0",
  "info": {
    "name": "My Package",
    "description": "A short description",
    "tags": ["tool", "utility"],
    "avatar_url": "https://example.com/avatar.png"
  },
  "variants": [
    {
      "label": "",
      "platform": "win-x64",
      "dependencies": {
        "github.com/other/package": ">=1.0.0"
      },
      "assets": [
        {
          "type": "zip",
          "urls": ["https://example.com/release.zip"],
          "placements": [
            {
              "type": "dir",
              "src": "bin/",
              "dest": "bin/"
            }
          ]
        }
      ],
      "preserve_files": ["config/*.json"],
      "remove_files": ["temp/"],
      "scripts": {
        "pre_install": ["echo pre-install"],
        "post_install": ["echo post-install"],
        "pre_uninstall": ["echo pre-uninstall"],
        "post_uninstall": ["echo post-uninstall"]
      }
    }
  ]
}
```

## Fields

### Top-Level

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `format_version` | `int` | Yes | Must be `3`. |
| `format_uuid` | `string` | Yes | Must be `"289f771f-2c9a-4d73-9f3f-8492495a924d"`. |
| `tooth` | `string` | Yes | Package path. Must be a valid [Go module path](https://go.dev/ref/mod#module-path). |
| `version` | `string` | Yes | Package version. Must be a valid [semantic version](https://semver.org/). |
| `info` | `object` | No | Package metadata. See [Info](#info). |
| `variants` | `array` | No | Package variants. See [Variant](#variant). |

### Info

| Field | Type | Default | Description |
| --- | --- | --- | --- |
| `name` | `string` | `""` | Display name. |
| `description` | `string` | `""` | Short description. |
| `tags` | `string[]` | `[]` | Tags. Each tag must match `^[a-z0-9-]+(:[a-z0-9-]+)?$`. |
| `avatar_url` | `string` | `null` | Avatar image URL. |

### Variant

A variant defines platform-specific or labeled package configurations. When resolving a variant, lip merges all variants whose `label` and `platform` match.

| Field | Type | Default | Description |
| --- | --- | --- | --- |
| `label` | `string` | `""` | Variant label. |
| `platform` | `string` | `""` | Target platform ([.NET RID](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog)). Empty matches all. Supports glob matching. |
| `dependencies` | `object` | `{}` | Map of package ID to [semver range](https://github.com/WalkerCodeRanger/semver#version-ranges). |
| `assets` | `array` | `[]` | Asset definitions. See [Asset](#asset). |
| `preserve_files` | `string[]` | `[]` | Glob patterns for files to preserve during uninstall. Patterns are matched against paths relative to the working directory, falling back to the bare file name. |
| `remove_files` | `string[]` | `[]` | Glob patterns for additional files to remove during uninstall. Patterns are matched against paths relative to the working directory; a pattern ending with `/` matches a directory. |
| `scripts` | `object` | `{}` | Lifecycle scripts. See [Scripts](#scripts). |

### Asset

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `type` | `string` | Yes | Archive type: `"self"`, `"tar"`, `"tgz"`, `"uncompressed"`, or `"zip"`. |
| `urls` | `string[]` | No | Download URLs. |
| `placements` | `array` | No | File placement rules. See [Placement](#placement). |

### Placement

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `type` | `string` | Yes | `"file"` or `"dir"`. |
| `src` | `string` | Yes | Source path within the archive. |
| `dest` | `string` | Yes | Destination path relative to the workspace. Must not be absolute, rooted, or contain `..`. |

### Scripts

| Field | Type | Default | Description |
| --- | --- | --- | --- |
| `pre_install` | `string[]` | `[]` | Commands to run before installation. |
| `install` | `string[]` | `[]` | Alias for `post_install`. Runs after package files are placed. |
| `post_install` | `string[]` | `[]` | Commands to run after installation. |
| `pre_uninstall` | `string[]` | `[]` | Commands to run before uninstallation. |
| `uninstall` | `string[]` | `[]` | Alias for `pre_uninstall`. Runs before installed files are removed. |
| `post_uninstall` | `string[]` | `[]` | Commands to run after uninstallation. |

Prefer `post_install` and `pre_uninstall` in new manifests. `install` and `uninstall` are supported as compatibility aliases for the same lifecycle phase. If both forms are present, lip runs both lists in that phase.
