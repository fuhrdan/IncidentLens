#!/usr/bin/env sh
# FOR LOCAL TESTING ONLY. A self-signed cert causes a browser warning.
set -eu
mkdir -p deploy/certs
chmod 700 deploy/certs
openssl req -x509 -newkey rsa:3072 -sha256 -days 7 -nodes \
  -keyout deploy/certs/privkey.pem -out deploy/certs/fullchain.pem \
  -subj '/CN=localhost' -addext 'subjectAltName=DNS:localhost,IP:127.0.0.1'
chmod 600 deploy/certs/privkey.pem
printf '%s\n' 'Local-only cert created. Replace before deployment.'
