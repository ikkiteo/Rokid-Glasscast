# GlassCast wire protocol v1

One TCP connection carries video one way and control the other way. The glasses are the
**server**: they listen, so every sender (phone, PC) only needs to connect. All integers are
little-endian.

## Discovery (UDP, port 47811)

A sender broadcasts the 8-byte probe `GCASTQ?\n` to `255.255.255.255:47811`.
Every listening receiver answers with a JSON datagram:

```json
{"app":"glasscast","name":"Rokid","port":47810,"w":480,"h":640,"fps":60,"v":1}
```

`w`/`h` are the panel size in its natural orientation. A sender uses them to pick a capture
size that needs no scaling on the glasses. Manual IP entry stays available: discovery is a
convenience, never a requirement.

## Session start (TCP, port 47810)

The sender opens the socket with `TCP_NODELAY` and writes a header once:

| field   | size | value                          |
|---------|------|--------------------------------|
| magic   | 4    | `GCS1`                         |
| jsonLen | 2    | length of the JSON that follows |
| json    | n    | see below                      |

```json
{"src":"pc","name":"desktop","w":1280,"h":720,"fps":60,"codec":"h264","rotate":0}
```

`codec` is `h264` (phone) or `mjpg` (Windows fallback). JPEG frames are sent as kind=1, one complete picture each.

`rotate` is a hint in degrees (0/90/180/270) telling the receiver how the sender expects the
picture to sit on the panel; the wearer can always override it.

## Video frames (sender to receiver)

| field | size | value |
|-------|------|-------|
| kind  | 1    | 0 = codec config (SPS/PPS), 1 = keyframe, 2 = delta, 3 = keepalive (size 0) |
| size  | 4    | payload length |
| ptsUs | 8    | presentation stamp in microseconds, sender clock |
| bytes | size | H.264 Annex-B |

Codec config is sent before the first keyframe and again after every reconnect. A keepalive
carries no payload and exists so the receiver can tell a frozen screen from a dead link.

## Control (receiver to sender)

| field | size | value |
|-------|------|-------|
| cmd   | 1    | 1 = send a keyframe now, 2 = set bitrate, 3 = set fps, 4 = stop |
| arg   | 4    | kbps for cmd 2, frames per second for cmd 3, otherwise 0 |

The receiver asks for a keyframe when it joins mid-stream or when it had to drop frames, so
recovery costs one frame instead of a reconnect.
