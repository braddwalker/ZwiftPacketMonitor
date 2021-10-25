# Zwift Companion App Messages

Using https://deprotobuf.com/ we can decode the messages sent by the Zwift Companion app to the Zwift app.

## Section 1: Zwift App to Zwift Companion App

Tag 2 contails the optional `ZwiftCompanionMessage2` which has a type indicator in tag 2 (mapped to field `Type` in the C# class).

Based on that type we can recognize the following payloads sent by the Zwift Companion app:

### Type 14

No clue, all tags seem to only have zero values.

### Type 16

We get lots of these, not yet clear what they contain, however the following we do know:

Tag 5 contains a `rider_id`, so it may mean that type 16 messages are _riders nearby_ although that's strange because this is traffic from the Zwift Companion app to the main Zwift app.

### Type 20

This message only has a non-zero value in tag 8.

### Type 22

Has a value in tag 10 and it always seems to be 1010 (uint32) or 505 (int32).

### Type 28

This message only has a non-zero value in tag 19 and it contains the main player `rider_id`.

### Type 29: App version / device type

This payload type can either contain the details about the app and the device it's running on.
The data is captured in the `ZwiftCompanionDevice` class and is comprised of tags 1 to 5.
The data is contained in tag 21 of `ZwiftCompanionMessage2`.

The tricky thing is that this message type can also contain data in tag 16 but not anything else. It's unclear what this field means.

### Notes

It looks like tag 6 of the `ZwiftCompanionMessage2` payload is always empty (just `{}`).

## Section 2: Zwift Companion App to Zwift App

Tag 1 can either be the `rider_id` but also 1, or 2.

### Tag 1 value 1

This message doesn't have a tag 10.



### Notes

blah

## Sequences

| seq | direction | type  | notes |
| --- | --------- | ----- | ----- |
| 0   | app to pc | Zwift |


### In the PCAP dumps

192.168.1.53 - pc
192.168.1.55 - mobile device

DST port 21587 is the port on the mobile device.

=> `outgoing` packets are the packets that flow from the Zwift Companion app to the Zwift Desktop app
=> `incoming` packets are the packets that flow from the Zwift Desktop app to the Zwitft Companion app
