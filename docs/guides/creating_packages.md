# Creating Packages

This guide walks you through creating a new package with lip.

## Using `lip init`

The easiest way to start a new project is using the `lip init` command.

```sh
lip init
```

This command creates a basic `tooth.json` file in your current directory. If a `tooth.json` already exists there, it is overwritten.

The generated `tooth.json` looks like this:

```json
{
  "format_version": 3,
  "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
  "tooth": "github.com/user/repo",
  "version": "0.0.0",
  "info": {
    "name": "",
    "description": "",
    "tags": [],
    "avatar_url": ""
  },
  "variants": []
}
```

You should edit the `"tooth"` field to match your actual repository path or package identifier.

## Manual Creation

You can also create a `tooth.json` file manually. Ensure you include the required fields:

-   `format_version`: Currently `3`.
-   `format_uuid`: Must be `"289f771f-2c9a-4d73-9f3f-8492495a924d"`.
-   `tooth`: The package path. Must be a valid [Go module path](https://go.dev/ref/mod#module-path), such as `github.com/user/repo`.
-   `version`: A valid semantic version.

Example:

```json
{
  "format_version": 3,
  "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
  "tooth": "github.com/user/my-package",
  "version": "1.0.0",
  "info": {
    "description": "My awesome package"
  }
}
```

Once created, you can start adding dependencies using `lip install`.
