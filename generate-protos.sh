#!/bin/bash

protoc --proto_path=src --csharp_out=src ./src/zwiftCompanionMessages.proto
protoc --proto_path=src --csharp_out=src ./src/zwiftMessages.proto