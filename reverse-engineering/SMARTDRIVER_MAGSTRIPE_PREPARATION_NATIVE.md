# smartDriver magnetic-track preparation helper recovery

This note closes the previously unresolved native helper `0x00603FC0` referenced by `SMARTDRIVER_ENCODECHIP_NATIVE.md`. It records the directly recovered data flow used by both Optional-gated magnetic-stripe sites in `smartDriver.StampaRecord`. No managed code in this slice executes the helper or any printer-mutating API.

## Call sites

`smartDriver.StampaRecord` calls helper `0x00603FC0` twice, once in each structurally matching Optional-gated magnetic-stripe stage. Both calls pass a numeric Variant value `1`. The caller compares the returned Variant with numeric zero through `__vbaVarTstNe`; only a nonzero result reaches `EncodeMagStripeWithApi`.

The helper is therefore not an arbitrary Boolean probe. Its return value is derived from the prepared magnetic-track data described below.

## Recovered Traccia1 / Traccia2 / Traccia3 flow

The helper consumes the three legacy magnetic-track globals in order:

- `Traccia1`
- `Traccia2`
- `Traccia3`

For each track it prepares the value and writes it into the corresponding `frmCarta` track property. The recovered getter/setter vtable pairs are:

| Global | getter | setter |
| --- | ---: | ---: |
| `Traccia1` | `+0x75C` | `+0x760` |
| `Traccia2` | `+0x768` | `+0x76C` |
| `Traccia3` | `+0x774` | `+0x778` |

The exact internal text transformation performed while preparing each global is still not assigned a source-level name here. What matters for the surrounding `StampaRecord` gate is the value after it has been stored in `frmCarta` and read back through the corresponding getter.

## Exact return contract

After the three prepared values have been written, the helper reads the three `frmCarta` getters and evaluates their VB `Len(...)` values. Its returned numeric Variant is equivalent to:

```text
Len(prepared Track 1) + Len(prepared Track 2) + Len(prepared Track 3)
```

Consequently the `StampaRecord` test immediately after `0x00603FC0` has the exact high-level meaning:

```text
preparedTrackDataLength != 0
```

That is, the subsequent `EncodeMagStripeWithApi` call is eligible only when at least one of the three prepared magnetic tracks contains data. If all three prepared values are empty, the encode call is skipped.

This is stronger than the previous description of `0x00603FC0` as an unnamed preparation helper with an unknown nonzero result. The source-level procedure name remains unrecovered, but its surrounding data/return semantics are now sufficiently proven for a non-executing compatibility model.

## Managed compatibility boundary

`LegacySmartDriverMagstripePreparationSemantics` records:

- native helper address `0x00603FC0`;
- smartDriver raw argument `1`;
- the `Traccia1/2/3` source names;
- exact `frmCarta` getter/setter vtable pairs;
- the returned sum-of-three-lengths contract;
- the resulting `!= 0` data-present gate.

The managed model accepts already-derived track lengths instead of attempting to reproduce unresolved VB6 Variant/string coercion or the internal preparation transformation. It performs no `ICE_API`, GDI, COM, `EncodeMagStripeWithApi`, printer, or card mutation.
