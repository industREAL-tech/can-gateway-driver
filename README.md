# industREAL.CAN.CANGW

.NET driver for the **industREAL Gen1** USB/Ethernet to CAN-FD gateway.
LGPL-3.0-only, `net8.0`.

```bash
dotnet add package industREAL.CAN.CANGW
```

## Two APIs

| | Namespace | Status |
|---|---|---|
| **v2** | `industREAL.CAN.CANGW.V2` | Current. Use this. |
| v1 | `industREAL.CAN` (`CanGateway`) | Original API, kept for existing integrations. |

### v2 — recommended

UDP transport, COBS-framed envelopes with a CRC-16/CCITT-FALSE, two channels,
NTP-style clock sync, and control commands for bit timing, termination and
identify.

```csharp
using industREAL.CAN.CANGW.V2;

var client = new CanGwClient(new UdpTransport("10.0.0.92", 12144));
client.FrameReceived += f => Console.WriteLine($"{f.Channel} 0x{f.CanId:X} {f.Data.Length} B");

await client.ConnectAsync();
await client.OpenChannelAsync(CanChannel.A, ChannelMode.Owned);
await client.SetBaudAsync(CanChannel.A, 500_000, 2_000_000, CanMode.FdBrs);
await client.SendFrameAsync(CanChannel.A, 0x123, CanFlagsV2.None, new byte[] { 1, 2, 3, 4 });
```

`CanGwClient.Clock` exposes the device-to-host clock fit; it returns `null` for a
timestamp until it has a fix rather than substituting host time.

## Wire format (v2)

```
[VER=0x01][TYPE][CHANNEL][SEQ:2 LE][LEN:2 LE][PAYLOAD][CRC16-CCITT-FALSE:2 LE]
```

COBS-encoded with a single `0x00` terminator, one envelope per datagram.
`MaxPayload` is 1200 bytes.

## Scope

This package targets the **Gen1** hardware only.

For the **GW2** (Gen2, Zynq-based) gateway — CAN-FD on a shared hardware
timebase, RS485 telegrams, passive bitrate autodetect, fault injection — use
`industREAL.CAN.ADVCANGW`, which speaks wire protocol v3. It is a separate
package with a separate protocol; there is no upgrade path between the two
clients and none is planned.

An earlier revision of this repository carried a copy of that v3 client in a
`V3/` folder. It was never released, is not part of any published package, and
has been removed: it duplicated `industREAL.CAN.ADVCANGW` type-for-type, forcing
consumers of both to use an `extern alias`, and it retained three protocol bugs
that were fixed upstream.

## Fixed in 0.2.0

Two bugs in the COBS codec, both needing 254 or more consecutive non-zero bytes.
`MaxPayload` is 1200, so both were reachable by a large frame batch; they went
unnoticed because real traffic stayed short and zero-sprinkled.

- **`Encode`** never closed a full block with a `0xFF` code, so the block length
  overflowed a single byte and produced a stream the matching decoder could not
  read.
- **`Decode`** sized its output buffer as `input.Length - 2`, which is exact only
  when the frame contains no full block. Each `0xFF` block left one extra byte on
  the end, and the envelope then failed its CRC.

If you were sending large frames on v2, this is the reason. Both are pinned by
`tests/`, with the boundary asserted at 253 and 254 bytes.

Also in 0.2.0: `CanGateway` treated `_flags == CanFlags.Control` as a control
frame test, but `Control` is a bit at `0x8000_0000`, so the comparison only
matched when no other flag was set. It is now a mask test.

## Building

```bash
dotnet build
dotnet run --project tests    # codec self-checks, no hardware needed
```

`industREAL.CAN.CORE` is expected beside this repository. The project probes for
both the mirror layout (`../industREAL.CAN.CORE/`) and the internal umbrella
layout (`../core/`), so the same csproj builds in either without edits.
