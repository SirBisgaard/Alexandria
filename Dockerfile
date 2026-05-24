FROM catthehacker/ubuntu:act-latest

RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && chmod +x /tmp/dotnet-install.sh \
 && /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/local/dotnet \
 && rm /tmp/dotnet-install.sh
ENV PATH="/usr/local/dotnet:${PATH}"
ENV DOTNET_ROOT=/usr/local/dotnet

COPY --from=mwader/static-ffmpeg:latest /ffmpeg /ffprobe /usr/local/bin/

WORKDIR /src
