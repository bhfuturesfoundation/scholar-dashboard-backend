# ---------------------------
# STAGE 1: Build
# ---------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only the project files first, so the restore layer is cached until a csproj changes.
#
# The .sln is deliberately NOT copied. A bare `dotnet restore` next to a solution file
# restores the whole solution, which means every project listed in it must be present —
# and the runtime image has no reason to carry the test project. Copying the .sln made the
# image build fail with MSB3202 the moment Auth.Tests was added to the solution, and
# because the build failed the platform simply kept serving the previous image: the API
# looked fine while silently running weeks-old code.
COPY Auth.API/Auth.API.csproj Auth.API/
COPY Auth.Models/Auth.Models.csproj Auth.Models/
COPY Auth.Services/Auth.Services.csproj Auth.Services/

# Restore the API explicitly. Its project references pull in Auth.Models and Auth.Services,
# so this covers everything the image needs and nothing it doesn't — adding another
# test-only or tooling project to the solution can no longer break the deploy.
RUN dotnet restore Auth.API/Auth.API.csproj

# Copy the rest of the code
COPY . .

# Publish the API project by path rather than by working directory, and skip restore since
# the cached layer above already did it.
RUN dotnet publish Auth.API/Auth.API.csproj -c Release -o /app/publish --no-restore

# ---------------------------
# STAGE 2: Runtime
# ---------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# postgresql-client supplies pg_dump, which the backup service shells out to for the one
# format that captures schema as well as data — the format you actually restore from.
# Without it the other three backup formats still work (they are pure C#), and the UI
# reports pg_dump as unavailable rather than failing at the point of use.
#
# Pulled from PGDG rather than Debian's own repo because pg_dump must be at least the
# server's major version, and Debian bookworm ships 15 while Railway runs 16+.
#
# Deliberately non-fatal. pg_dump is optional — the other three backup formats are pure C#
# and the service reports pg_dump as unavailable when the binary is missing. Letting an
# external apt repository be able to fail the image build would mean a PGDG outage or key
# rotation takes down deploys of the entire API to gain nothing. If this step fails the
# image still ships; only the pg_dump backup format is unavailable.
RUN set -eux; \
    ( apt-get update \
      && apt-get install -y --no-install-recommends curl ca-certificates gnupg \
      && install -d /usr/share/postgresql-common/pgdg \
      && curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc \
           -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc \
      && echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt bookworm-pgdg main" \
           > /etc/apt/sources.list.d/pgdg.list \
      && apt-get update \
      && apt-get install -y --no-install-recommends postgresql-client-16 \
    ) || echo "WARNING: postgresql-client could not be installed; the pg_dump backup format will report as unavailable."; \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Railway expects port 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Auth.API.dll"]
