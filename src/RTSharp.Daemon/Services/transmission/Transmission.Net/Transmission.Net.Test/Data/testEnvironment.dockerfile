FROM mcr.microsoft.com/dotnet/sdk:6.0-focal

RUN apt-get update && apt-get install -y software-properties-common &&\
    add-apt-repository -y ppa:transmissionbt/ppa &&\
    apt-get update && apt-get install -y transmission-daemon &&\
    apt-get remove -y apt-transport-https software-properties-common &&\
    apt-get clean && rm -rf /var/lib/apt/lists/*

RUN wget https://aka.ms/getvsdbgsh && \
    sh getvsdbgsh -v latest  -l /usr/local/.vsdbg

RUN echo '#!/bin/sh\n\
    set -e;\n\
    if [ "${1#-}" != "$1" ]; then\n\
    set -- transmission-daemon "$@"\n\
    fi\n\
    exec "$@"' > /usr/local/bin/entrypoint &&\
    chmod +x /usr/local/bin/entrypoint

ENTRYPOINT [ "/usr/local/bin/entrypoint" ]

CMD [ "transmission-daemon", "--config-dir", "/var/lib/transmission-daemon/info", "-T", "--foreground" ]
