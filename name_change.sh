#!/bin/bash
rm -rf .git
find . -type f -exec sed -i "s/BPMCOScraper/$1/g" {} +
find . -type f -exec bash -c "mv \$0 \${0/BPMCOScraper/$1}" {} \;