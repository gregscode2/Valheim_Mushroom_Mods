"""Simulate BepInEx 5 process-filter matching for RandomYggdrasil.

BepInEx 5.4 Chainloader:
  Paths.ProcessName = GetFileNameWithoutExtension(executablePath)
  skip if every BepInProcess filter fails:
    filter.Replace(".exe", "") equals ProcessName (case-insensitive)

If this plugin is skipped, Awake never runs and RandomYggdrasil.cfg is never written.
"""
from __future__ import print_function

import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MOD_SOURCE = os.path.join(REPO_ROOT, "RandomYggdrasil", "RandomYggdrasilMod.cs")

# Executables that must load this plugin. Keys are Paths.ProcessName as BepInEx computes it.
REQUIRED_PROCESSES = {
    "valheim": "client valheim.exe",
    "valheim_server": "dedicated server (Windows valheim_server.exe or Linux valheim_server.x86_64)",
}


def net_file_name_without_extension(path):
    name = os.path.basename(path.replace("\\", "/"))
    last_dot = name.rfind(".")
    if last_dot <= 0:
        return name
    return name[:last_dot]


def parse_bepin_process_filters(source_path):
    with open(source_path, "r") as handle:
        text = handle.read()
    return re.findall(r'\[BepInProcess\("([^"]+)"\)\]', text)


def matches_process(filters, process_name):
    if not filters:
        return True
    return any(
        filter_name.replace(".exe", "").lower() == process_name.lower()
        for filter_name in filters
    )


def main():
    if not os.path.isfile(MOD_SOURCE):
        print("FAIL: missing {}".format(MOD_SOURCE))
        return 1

    filters = parse_bepin_process_filters(MOD_SOURCE)
    print("BepInProcess filters: {}".format(filters if filters else "(none — loads in every process)"))

    linux_server_process = net_file_name_without_extension("valheim_server.x86_64")
    windows_server_process = net_file_name_without_extension("valheim_server.exe")
    print("BepInEx ProcessName for valheim_server.x86_64: {}".format(linux_server_process))
    print("BepInEx ProcessName for valheim_server.exe: {}".format(windows_server_process))

    failed = False
    for process_name, description in REQUIRED_PROCESSES.items():
        loaded = matches_process(filters, process_name)
        status = "LOAD" if loaded else "SKIP"
        print("{}  process='{}' ({})".format(status, process_name, description))
        if not loaded:
            failed = True

    if linux_server_process != "valheim_server" or windows_server_process != "valheim_server":
        print("FAIL: ProcessName derivation does not match BepInEx/GetFileNameWithoutExtension")
        failed = True

    if failed:
        print("RED: dedicated server would skip this plugin, so RandomYggdrasil.cfg is never created")
        return 1

    print("GREEN: plugin would load on client and dedicated server")
    return 0


if __name__ == "__main__":
    sys.exit(main())
