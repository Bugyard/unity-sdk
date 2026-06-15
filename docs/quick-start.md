# Quick start

## Install

In Unity: **Window → Package Manager → + → Add package from git URL…**

```
https://github.com/bugyard/bugyard-unity.git
```

To pin a version, append a tag:

```
https://github.com/bugyard/bugyard-unity.git#v0.1.0
```

Or add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.bugyard.sdk": "https://github.com/bugyard/bugyard-unity.git#v0.1.0"
  }
}
```

## Set up

1. Create a config asset: **Tools → Bugyard → Create Config Asset**.
2. Select it and set:
    - `apiKey` — your project key (e.g. `by_pk_test_xxx`). Create it in the
      [Bugyard dashboard](https://github.com/bugyard/bugyard#readme)
      (Project → Settings → API keys).
    - `endpoint` — your backend base URL (no trailing `/v1`). The default
      `https://api.bugyard.com` points at the hosted backend; self-hosters set
      their own. See the [backend setup guide](https://github.com/bugyard/bugyard#readme).
3. Initialize once at startup and let the hotkey do the rest:

```csharp
using UnityEngine;
using BugyardSDK;

public class Bootstrap : MonoBehaviour
{
    [SerializeField] private BugyardConfig config;

    void Awake() => Bugyard.Init(config);
}
```

!!! tip "Verify connectivity before shipping"
    **Tools → Bugyard → Send Test Report** uploads a synthetic report with the
    current settings and reports success (with a dashboard link) or the precise
    failure reason.

Press **F8** in play mode to open the report overlay. Fill it in, hit **Send**.

## Without a config asset (prototyping)

```csharp
Bugyard.Init("by_pk_test_xxx", "https://api.bugyard.com");
```

This builds a config in memory — convenient for a quick spike, but a config asset
is recommended so you can tune capture behaviour (see
[Configuration](configuration.md)).

## Next steps

- Drive the SDK from your own UI or gameplay code — see
  [Programmatic triggers](api-reference.md#programmatic-triggers).
- Review what leaves the player — see [What gets sent](what-gets-sent.md).
