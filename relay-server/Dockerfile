FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env
WORKDIR /lrm
COPY ./ServerProject-DONT-IMPORT-INTO-UNITY ./
# net5.0 -> net8.0 (2026-07-21): the aspnet/sdk:5.0 images stopped resolving to a working
# runtime (.NET 5 has been EOL since May 2022) and the relay crash-looped in prod with
# "Microsoft.NETCore.App 5.0.0 not found". linux-x64 replaces the old ubuntu.20.04-x64 RID,
# which the modern RID graph no longer recognizes. --self-contained false keeps this
# framework-dependent, matching the separate aspnet runtime base image below.
RUN dotnet publish LRM.sln --runtime linux-x64 --self-contained false -c Release -o /out/

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /lrm
COPY --from=build-env /out/ .

ENV NO_CONFIG="true"
ENV TRANSPORT_CLASS="kcp2k.KcpTransport"
ENV AUTH_KEY="Secret Auth Key"
ENV TRANSPORT_PORT="7777"
ENV UPDATE_LOOP_TIME="10"
ENV UPDATE_HEARTBEAT_INTERVAL="100"
ENV RANDOMLY_GENERATED_ID_LENGTH="5"
ENV USE_ENDPOINT="true"
ENV ENDPOINT_PORT="8080"
ENV ENDPOINT_SERVERLIST="true"
ENV ENABLE_NATPUNCH_SERVER="true"
ENV NAT_PUNCH_PORT="7776"
ENV USE_LOAD_BALANCER="false"
ENV LOAD_BALANCER_AUTH_KEY="AuthKey"
ENV LOAD_BALANCER_ADDRESS="127.0.0.1"
ENV LOAD_BALANCER_PORT="7070"
ENV LOAD_BALANCER_REGION="1"
ENV KCP_NODELAY="true"
ENV KCP_INTERVAL="10"
ENV KCP_FAST_RESEND="2"
ENV KCP_CONGESTION_WINDOW="false"
ENV KCP_SEND_WINDOW_SIZE="4096"
ENV KCP_RECEIVE_WINDOW_SIZE="4096"
ENV KCP_CONNECTION_TIMEOUT="10000"

EXPOSE 7777/udp
EXPOSE 7776/udp
EXPOSE 8080

ENTRYPOINT [ "./LRM" ]
