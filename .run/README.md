# Rider configurations

Open `RaceImperialis.slnx` in Rider. All configurations build before Run/Debug.

| Configuration | Starts |
| --- | --- |
| `Race Imperialis: https` | Pine Loop at `https://localhost:7280` |
| `Race Imperialis: tests` | Game integration checks |
| `WolfSlop: tests` | Portable engine checks |
| `WolfSlop: API checks` | Independent engine API/UI checks |

Initialize the Engine submodule before opening the solution. All project paths
are relative to this repository, and the web profile uses HTTPS only.
