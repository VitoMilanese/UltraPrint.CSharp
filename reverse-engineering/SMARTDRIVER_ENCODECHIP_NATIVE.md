# smartDriver EncodeChip continuation recovery

This note isolates the post-`EncodeChip` control flow in UltraPrint 2.2.115 `smartDriver.StampaRecord` (`0x00512D20`). It records only branch predicates, raw ICE arguments and public-module state that are directly supported by native evidence. It does not assign hardware semantics to the raw `SmartCardContinue` values and does not enable printer mutation.

## `EncodeChip` result test

After `_FeedCard(hDC, 0x11)` succeeds, `StampaRecord` dispatches the layout script hook `Funzioni.Vbscript("EncodeChip", ...)`. The returned Variant is preserved at `[ebp-0xC4]`.

At `0x00513F7D` native code pushes that returned Variant, then builds and pushes a literal numeric zero Variant (`VT_I2`) at `0x00513F89`-`0x00513F9C`, and calls `__vbaVarTstLt` at `0x00513F9F`. The VB6 runtime helper receives the first pushed Variant as the left operand and the second as the right operand, so the recovered predicate is:

```text
EncodeChipResult < 0
```

The managed compatibility model deliberately accepts a signed numeric result only. It does not attempt to reproduce all VB6 Variant coercion/error behavior for arbitrary script return types.

## Negative-result branch

When `EncodeChipResult < 0` is true, the native path beginning at `0x00513FAE`:

1. obtains `Printer.hDC`;
2. calls `_SmartCardContinue@8(hDC, 1)` at `0x0051401D`;
3. calls GDI `EndPage`;
4. dispatches script hook `EndPage`;
5. calls GDI `EndDoc`;
6. dispatches script hook `EndDoc`.

Thus raw continue value `1` is proven for this branch, and page/document closure is immediate. The vendor meaning of value `1` remains deliberately unnamed.

## Zero-or-positive branch

When the `< 0` test is false, native execution reaches `0x005144B1` and:

1. writes VB True (`0xFFFF`) to the WORD at `0x0062B682`;
2. obtains `Printer.hDC`;
3. calls `_SmartCardContinue@8(hDC, 0)` at `0x00514529`;
4. continues into optional magnetic-stripe and generic module processing instead of immediately closing the document.

Therefore the proven split is:

| `EncodeChip` numeric result | Public Boolean `0x62B682` effect | raw `SmartCardContinue` value | immediate `EndPage` / `EndDoc` |
| --- | --- | ---: | --- |
| `< 0` | no write on this branch | `1` | yes |
| `>= 0` | set to VB True | `0` | no |

The same result-sign contract is independently repeated by the generic native helper at `0x005F2540`: its `__vbaVarTstLt` call at `0x005F28DE` returns early for a negative result and sets `0x0062B682 = True` at `0x005F2921` for a non-negative result.

## Ownership of the two transient WORD Booleans

The two addresses previously described only as transient smartDriver flags are not fields of the `smartDriver` form/class. VB6 metadata places them in the public-data slab of the standard BAS module named `smartdriverExamples`:

- public object descriptor: `0x0042675C`;
- module name: `smartdriverExamples` (`0x0042734C`);
- `lpPublicBytes`: `0x0042A498`;
- `lpModulePublic`: `0x0062B670`;
- first transient WORD Boolean: module-public offset `+0x10` => `0x0062B680`;
- second transient WORD Boolean: module-public offset `+0x12` => `0x0062B682`.

Both are read/written as 16-bit values and native True is `0xFFFF`, consistent with VB Boolean storage. Their source-level variable names are not recovered, so managed code keeps neutral `FirstTransientBoolean` / `SecondTransientBoolean` terminology instead of inventing names.

`StampaRecord` resets the second Boolean to False at `0x005135D2`. Before `_FeedCard`, it checks the same Boolean at `0x00513C98`; nonzero exits to final cleanup at `0x005154C4`. This proves a pre-feed transient gate, but not the user-facing or hardware meaning of that state.

## Rear-side / fallback raw value `1`

A third `_SmartCardContinue@8(hDC, 1)` call exists at `0x00514FC1`. The path is reached by the later rear-side continuation and also by the direct fallback jump from a zero `_FeedCard(hDC, 0x11)` result. After that call native code again executes the `EndPage` / script `EndPage` / `EndDoc` / script `EndDoc` closure sequence.

`frmCarta.HasRear` is read through vtable offset `+0x794` at `0x00514DCE`; false jumps directly to final cleanup, while true permits the later rear-side processing path. This proves the gate and the raw call sequence without assigning a semantic enum name to `SmartCardContinue(1)`.

## Cleanup

At `0x005154C4` final cleanup always:

- clears `0x0062B682` to VB False;
- clears `0x0062B680` to VB False;
- calls `_SetInteractiveMode@8(hDC, FALSE)` at `0x00515541`;
- later writes `frmCarta.InteractiveMode = False`.

The C# layer keeps all of the above as pure data/state semantics. No new path in this recovery slice executes `_SmartCardContinue`, `EndPage`, `EndDoc`, `_FeedCard`, magnetic-stripe APIs, or any other printer-mutating call.
