# Subclassing OCCT classes

A few OCCT classes are meant to be derived from. C# derives from these as from any class, and OCCT calls the C# overrides:

| Class | Override | For |
|---|---|---|
| `Message_ProgressIndicator` | `Show`, `UserBreak` | progress and cancellation of booleans, meshing, reading files |
| `Message_Printer` | `send` | OCCT's messages, in your log |
| `AIS_InteractiveObject` | `Compute`, `ComputeSelection`, and its other virtual members | presentations of your own, selectable |
| `SelectMgr_EntityOwner` | `HilightWithColor`, `Unhilight`, `IsForcedHilight`, ... | what a selection picks, and how it highlights |
| `AIS_ViewController` | `OnSelectionChanged`, `OnObjectDragged`, `handleViewRedraw`, ... | a view's input |

OCCT's protected members stay protected (`Compute`, `Show`, `send`), and so do protected constructors: only a subclass calls them. A member a subclass doesn't override runs OCCT's implementation. Other classes can't be derived from in C#: OCCT never calls a C# override of theirs.

## Progress and cancellation

[!code-csharp[](../samples/Subclassing.cs#progress)]

`Start()` gives the range an algorithm takes as its last parameter, a default C# may leave out. When `UserBreak` says so, the algorithm stops at its next report, with an error (`HasErrors()`). OCCT may call `Show` on its worker threads, one at a time.

## Messages

[!code-csharp[](../samples/Subclassing.cs#printer)]

[!code-csharp[](../samples/Subclassing.cs#messenger)]

## Presentations

[!code-csharp[](../samples/Subclassing.cs#presentation)]

[!code-csharp[](../samples/Subclassing.cs#display)]

`Compute` fills the presentation of a display mode, `ComputeSelection` the sensitive entities of a selection mode; the context calls them when it shows the object and when a mode is activated.

## What OCCT holds

- **Alive while OCCT holds it:** an object OCCT keeps a reference to lives without a C# variable: a printer only the messenger holds keeps printing. Once OCCT lets go, the garbage collector collects it.
- **Disposed while OCCT holds it:** released once OCCT lets go, so its overrides keep working until then.
- **Handed back as itself:** OCCT returning the object gives the same C# instance: `owner.Selectable()` is the `Marker` you made, not a new `SelectMgr_SelectableObject`.
- **Passed, not reached:** OCCT counts the references it takes of an object passed as an argument. One it takes through the object's own members goes unnoticed while C# still refers to the object; if C# lets go first, the object stays alive past its finalizer and a trace warning says so.
- `AIS_ViewController` isn't handle-managed: its proxy owns it, as with other classes, and its callbacks run while C# calls it.

## Exceptions

An exception in an override unwinds OCCT's call as a C++ exception would, and comes back as the `InnerException` of the `OcctException` the outer call throws:

[!code-csharp[](../samples/Subclassing.cs#exception)]

An exception OCCT catches itself goes no further, as in C++: an algorithm that catches failures records an error instead.

## What can't be overridden

Virtual members whose parameters or result don't reach C# (streams, collections, raw pointers, references returned), and those without parameters and result: SWIG runs no code after such a callback, where a C# exception would be rethrown. [Skipped members](skipped.md#overrides) lists them. C# still calls them; a subclass can't replace them.
