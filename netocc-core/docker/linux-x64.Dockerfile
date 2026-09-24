# linux-x64 build + test, then pack and consume a linux-x64 package. From netocc-core/:
#   docker build -f docker/linux-x64.Dockerfile -t netocc-linux .
#   docker run --rm -v netocc-vcpkg-linux:/src/.vcpkg netocc-linux
# The named volume keeps vcpkg's state (binary cache, installed OCCT) between runs, so
# OCCT builds once. A named volume, not a bind mount: building OCCT on a Windows path is slow.
# A local check: CI builds the shipped natives on ubuntu-22.04 (glibc 2.35, GCC 11); this image's need glibc 2.39.
FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive
# main + universe only: fewer indexes, and a mirror caught mid-sync ("Hash Sum mismatch") fails the whole update
RUN sed -i 's/^Components: .*/Components: main universe/' /etc/apt/sources.list.d/ubuntu.sources \
 && apt-get -o Acquire::Retries=5 update && apt-get install -y --no-install-recommends \
      build-essential cmake ninja-build git curl zip unzip tar pkg-config \
      python3 ca-certificates autoconf autoconf-archive automake libtool \
      libgl1-mesa-dev libglu1-mesa-dev libx11-dev libxext-dev libxi-dev \
      libicu74 \
    && rm -rf /var/lib/apt/lists/*

# .NET from Microsoft's installer, all in one dotnet root: the 10.0 SDK builds every target, and the
# 8.0 and 6.0 runtimes run their tests. Ubuntu 24.04 doesn't package .NET 6, which is out of support.
ENV DOTNET_ROOT=/usr/share/dotnet DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_ROOT" \
 && bash /tmp/dotnet-install.sh --channel 8.0 --runtime dotnet --install-dir "$DOTNET_ROOT" --skip-non-versioned-files \
 && bash /tmp/dotnet-install.sh --channel 6.0 --runtime dotnet --install-dir "$DOTNET_ROOT" --skip-non-versioned-files \
 && ln -s "$DOTNET_ROOT/dotnet" /usr/local/bin/dotnet \
 && rm /tmp/dotnet-install.sh \
 && dotnet --list-runtimes

# vcpkg at the registry baseline vcpkg-configuration.json pins, as in CI (../.github/actions/occt): the tool's scripts
# match the ports. The file comes in alone, so this layer rebuilds only when the baseline changes.
COPY vcpkg-configuration.json /tmp/vcpkg-configuration.json
RUN commit=$(python3 -c "import json; print(json.load(open('/tmp/vcpkg-configuration.json'))['default-registry']['baseline'])") \
 && git init --quiet /opt/vcpkg \
 && git -C /opt/vcpkg fetch --quiet --depth 1 https://github.com/microsoft/vcpkg.git "$commit" \
 && git -C /opt/vcpkg checkout --quiet FETCH_HEAD \
 && /opt/vcpkg/bootstrap-vcpkg.sh -disableMetrics
ENV VCPKG_ROOT=/opt/vcpkg

WORKDIR /src
COPY . /src

CMD ["/bin/sh", "-c", "python3 build.py tools && python3 build.py occt --triplet x64-linux-dynamic && python3 build.py generate && python3 build.py native --triplet x64-linux-dynamic && python3 build.py test --arch x64 && python3 build.py pack --rids linux-x64 && python3 build.py test-package --arch x64"]
