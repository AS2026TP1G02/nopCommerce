#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "$0")" && pwd)/common.sh"

log "RabbitMQ queue snapshot"
rabbitmq_queues
