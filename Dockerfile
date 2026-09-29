# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Build stage
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1) Copy only the project files first so that `dotnet restore` is cached
#    independently of source-code changes.
COPY src/Masuit.LuceneEFCore.SearchEngine/Masuit.LuceneEFCore.SearchEngine/Masuit.LuceneEFCore.SearchEngine.csproj \
     Masuit.LuceneEFCore.SearchEngine/Masuit.LuceneEFCore.SearchEngine/
COPY src/Masuit.Tools/Masuit.Tools.Abstractions/Masuit.Tools.Abstractions.csproj \
     Masuit.Tools/Masuit.Tools.Abstractions/
COPY src/Masuit.Tools/Masuit.Tools.Core/Masuit.Tools.Core.csproj \
     Masuit.Tools/Masuit.Tools.Core/
COPY src/Masuit.Tools/Masuit.Tools.AspNetCore/Masuit.Tools.AspNetCore.csproj \
     Masuit.Tools/Masuit.Tools.AspNetCore/
COPY src/Masuit.Tools/Masuit.Tools.Excel/Masuit.Tools.Excel.csproj \
     Masuit.Tools/Masuit.Tools.Excel/
COPY src/Masuit.MyBlogs.Core/Masuit.MyBlogs.Core.csproj Masuit.MyBlogs.Core/

# Directory.Build.props is picked up by MSBuild walking up from each project,
# so it must be present during restore as well (relative layout preserved).
COPY src/Masuit.Tools/Directory.Build.props Masuit.Tools/Directory.Build.props

RUN dotnet restore Masuit.MyBlogs.Core/Masuit.MyBlogs.Core.csproj

# 2) Copy the full sources and publish.
#    The admin SPA (wwwroot/dashboard) is already committed to the repo, so the
#    frontend build is not required here. See docs/DEPLOY.md if you prefer to
#    build the frontend inside the image instead.
COPY src/ ./
RUN dotnet publish Masuit.MyBlogs.Core/Masuit.MyBlogs.Core.csproj \
        -c Release \
        --no-restore \
        -o /app/publish

# ---------------------------------------------------------------------------
# Runtime stage
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Runtime dependencies of the app:
#   libfontconfig1, libfreetype6 - required by SkiaSharp's libSkiaSharp.so
#   fonts-dejavu-core            - without real system fonts
#                                  SKFontManager.FontFamilies is empty and the
#                                  captcha has no typeface to render with
#   p7zip-full                   - external 7z binary used by the archive tools
#   curl                         - container health-checks / manual debugging
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        curl \
        libfontconfig1 \
        libfreetype6 \
        fonts-dejavu-core \
        p7zip-full \
    && rm -rf /var/lib/apt/lists/*

# The application listens on these ports (see appsettings.json).
# 5000 = HTTP, 5001 = HTTPS (only used when Https:Enabled = true).
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TZ=Asia/Shanghai

COPY --from=build /app/publish ./

# Directories that must be writable at runtime (see docker-compose.yml volumes):
#   /app/logs           : application logs (Masuit.Tools LogManager)
#   /app/lucene         : Lucene search index
#   /app/wwwroot/upload : user uploads (UploadPath system setting)
#   /app/App_Data/cert  : optional HTTPS certificate (only when Https:Enabled = true)
RUN mkdir -p /app/logs /app/lucene /app/wwwroot/upload /app/App_Data/cert

EXPOSE 5000 5001

# The HTTPS endpoint is optional. Recommended: terminate TLS in front of the
# container (Nginx / Caddy / Cloudflare) and set Https:Enabled = false.
ENTRYPOINT ["dotnet", "Masuit.MyBlogs.Core.dll"]
