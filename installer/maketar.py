# Packs a folder into a .tar.gz under a top-level directory, with Unix file modes set
# (a tar made on Windows can't carry the executable bit any other way).
#   python maketar.py <src_dir> <out.tar.gz> <top_dir_name> <exec_name> [<exec_name> ...]
# Files whose name (not path) is listed get mode 0755; everything else 0644, folders 0755.
import os
import sys
import tarfile


def main():
    src, out, top = sys.argv[1], sys.argv[2], sys.argv[3]
    execs = set(sys.argv[4:])

    def fix(info):
        info.uid = info.gid = 0
        info.uname = info.gname = ""
        if info.isdir():
            info.mode = 0o755
        else:
            info.mode = 0o755 if os.path.basename(info.name) in execs else 0o644
        return info

    with tarfile.open(out, "w:gz", compresslevel=6) as tar:
        tar.add(src, arcname=top, filter=fix)


if __name__ == "__main__":
    main()
