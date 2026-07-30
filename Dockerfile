# ---------------------------
# STAGE 1: Build
# ---------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY *.sln ./
COPY Auth.API/Auth.API.csproj Auth.API/
COPY Auth.Models/Auth.Models.csproj Auth.Models/
COPY Auth.Services/Auth.Services.csproj Auth.Services/

# Restore dependencies
RUN dotnet restore

# Copy the rest of the code
COPY . .

# Publish API project
WORKDIR /src/Auth.API
RUN dotnet publish -c Release -o /app/publish

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
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates gnupg \
    && install -d /usr/share/postgresql-common/pgdg \
    && curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc \
        -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc \
    && echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] \
https://apt.postgresql.org/pub/repos/apt bookworm-pgdg main" \
        > /etc/apt/sources.list.d/pgdg.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client-16 \
    && apt-get purge -y --auto-remove curl gnupg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Railway expects port 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Auth.API.dll"]
