#!/bin/bash

##
# This file combines all the other .txt files in this directory into changelog.txt,
# or another file name given as optional argument.
#


# Determine script's absolute directory and use that for the paths, so the
# script can be executed from anywhere
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

DEFAULT_TARGET_FILE="changelog.txt"

# A different target file can be provided as argument
if [ $# -eq 1 ]; then
    TARGET_FILE="$SCRIPT_DIR/$1"
else
    TARGET_FILE="$SCRIPT_DIR/$DEFAULT_TARGET_FILE"
fi

if [ ! -f "$TARGET_FILE" ]; then
    echo "$TARGET_FILE doesn't exist yet, creating it"
    touch "$TARGET_FILE"
fi


echo ""
echo "Appending *.txt into $(basename "$TARGET_FILE"):"
for file in "$SCRIPT_DIR"/*.txt; do
    if [ -f "$file" ]; then
        # Skip target file(s) that we're appending into
        if [[ "$(basename "$file")" = "$DEFAULT_TARGET_FILE" ||
            "$(basename "$file")" = "$(basename "$TARGET_FILE")" ]]; then
            echo " - Skipping target file: $(basename "$file")"
            continue
        fi
        echo " + $(basename "$file")"

        # Strip path and extension from the file name and append it as heading for the entry
        echo "$(basename "${file%.*}")" >> "$TARGET_FILE"
        # Append file content
        cat "$file" >> "$TARGET_FILE"
        # Add a newline after each file's content
        echo "" >> "$TARGET_FILE"
    else
        echo "ERROR: $file is missing or not a proper file, skipping!"
    fi
done
echo ""

# Finished file name in upper case and without path as heading
echo "=== $(basename "$TARGET_FILE"):"
echo ""

# Print the finished file itself
cat "$TARGET_FILE"
