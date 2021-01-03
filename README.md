# ZwiftPacketMonitor
This project implements a UDP packet monitor for the Zwift cycling simulator. It listens for packets on a specific port of a local network adapter, and when found, deserializes the payload and dispatches events that can be consumed by the caller.

NOTE: Because this utilizes a network packet capture to intercept the UDP packets, your system may require this code to run using elevated privileges.

This project is a .Net Core port of the Node zwift-packet-monitor project (https://github.com/wiedmann/zwift-packet-monitor).
