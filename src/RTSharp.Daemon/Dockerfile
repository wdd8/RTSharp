FROM mcr.microsoft.com/dotnet/aspnet:10.0

# amd64 or arm64
ARG TARGETARCH

# mediainfo may be invoked at runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends mediainfo \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY docker-context/${TARGETARCH}/ ./
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh \
 && mv ./appsettings.json ./appsettings.example.json \
 && mkdir -p /etc/rtsharp-daemon

VOLUME /etc/rtsharp-daemon

ENTRYPOINT ["docker-entrypoint.sh"]
