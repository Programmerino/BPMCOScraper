{
  description = "BPMCOScraper";

  inputs.nixpkgs.url = "github:nixos/nixpkgs";

  inputs.flake-utils.url = "github:numtide/flake-utils";

  inputs.dotnet.url = "github:Programmerino/dotnet-nix";

  outputs = { self, nixpkgs, flake-utils, dotnet }:
    flake-utils.lib.eachSystem(["x86_64-linux" "aarch64-linux"]) (system:
      let
        pkgs = import nixpkgs { 
          inherit system;
        };
        name = "BPMCOScraper";
        version = "0.0.0";
        sdk = pkgs.dotnetCorePackages.sdk_5_0;

      in rec {
          devShell = pkgs.mkShell {
            DOTNET_CLI_HOME = "/tmp/dotnet_cli";
            buildInputs = defaultPackage.nativeBuildInputs ++ [sdk];
            DOTNET_ROOT = "${sdk}";
          };
    
          defaultPackage = dotnet.buildDotNetProject.${system} rec {
              inherit name;
              inherit version;
              inherit sdk;
              inherit system;
              src = ./.;
              lockFile = ./packages.lock.json;
              configFile =./nuget.config;

              nativeBuildInputs = [
                pkgs.clang_12
              ];

              nugetSha256 = "sha256-ekLS3H4KcQw6PXrM/bpjB9fvLATTsvtu9wE2NhDcl2Y=";
          };
      }
    );
}