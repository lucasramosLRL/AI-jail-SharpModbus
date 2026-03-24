FROM ubuntu:22.04

ENV DEBIAN_FRONTEND=noninteractive

# ===== BASE =====
RUN apt update && apt install -y \
    wget \
    curl \
    git \
    ca-certificates \
    gnupg \
    && rm -rf /var/lib/apt/lists/*

# ===== NODE 20 (correto) =====
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# ===== .NET =====
RUN wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && apt update \
    && apt install -y dotnet-sdk-8.0 \
    && rm -rf /var/lib/apt/lists/*

# ===== CLAUDE CODE =====
RUN npm install -g @anthropic-ai/claude-code

# ===== USUÁRIO SEGURO =====
RUN useradd -m -s /bin/bash aiuser

# pasta de trabalho
WORKDIR /workspace

# permissões
RUN chown -R aiuser:aiuser /workspace

# troca pro usuário seguro
USER aiuser

# shell padrão
CMD ["bash"]

# garantir diretórios
RUN mkdir -p /home/aiuser \
    && chown -R aiuser:aiuser /home/aiuser
