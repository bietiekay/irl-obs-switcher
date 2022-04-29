# IRL (in real life) OBS Switcher (and Proxy)

## Why? 
I need a small and simple tool which can proxy TCP and UDP connections, like RTMP or SRT live-streams, and can react upon the availability of these streams.

In simple terms:

This tool monitors a live-stream from your camera feed (SRT or RTMP) and when it detects a disconnect it can switch scenes in Open Broadcaster Studio / OBS (https://obsproject.com/)

## How?
This is a keep-it-simple portable console application that can run on Windows, Linux, MacOS. It can run natively or in a container. Whatever your infrastructure this should work somehow.

It offers a simple JSON formatted configuration file (config.json) where you define the TCP or UDP ports that you use to deliver RTMP (using TCP) or SRT (using UDP) streams to. 

Furthermore you configure the target IP and Port where you want the data to be delivered to.

### Limitations
Each remote client is mapped to a port of the local server therefore:
- The original IP of the client is hidden to the server the packets are forwarded to.
- The number of concurrent clients is limited by the number of available ports in the server running the proxy.

### Configuration
`config.json` contains a map of named forwarding rules, for instance :

    {
     "http": {
     "localport": 80,
     "localip":"",
     "protocol": "tcp",
     "forwardIp": "xx.xx.xx.xx",
     "forwardPort": 80,
     timeOut: 1
     },
    ...
    }

- *localport* : The local port the forwarder should listen to.
- *localip* : An optional local binding IP the forwarder should listen to. If empty or missing, it will listen to ANY_ADDRESS.
- *protocol* : The protocol to forward. `tcp`,`udp`, or `any`.
- *forwardIp* : The ip the traffic will be forwarded to.
- *forwardPort* : The port the traffic will be forwarded to.
- *timeOut* : The timeout in seconds to detect disconnects - this is the time after which scenes get changed
