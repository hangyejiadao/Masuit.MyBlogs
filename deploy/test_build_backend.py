import base64
import importlib.util
import json
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("build_backend", Path(__file__).with_name("build_backend.py"))
build_backend = importlib.util.module_from_spec(spec)
spec.loader.exec_module(build_backend)


class ReleaseMetadataTests(unittest.TestCase):
    def test_metadata_preserves_commit_messages_and_has_unique_versions(self):
        sha = "a" * 40
        second_sha = "b" * 40
        message = '中文提交\n\n包含 "引号"、\\ 和多行日志'
        def fake_git(*args):
            if args[0] == "rev-parse": return sha
            if args[0] == "rev-list":
                self.assertEqual(args, ("rev-list", "--max-count=2", "HEAD"))
                return "\n".join([sha, second_sha])
            if args[0] == "branch": return "master"
            if args[0] == "status": return " M Dockerfile"
            return {"--format=%an": "作者", "--format=%cI": "2026-10-03T12:00:00+08:00", "--format=%B": message}[args[2]]
        with patch.object(build_backend, "git", side_effect=fake_git), patch.dict(build_backend.os.environ, {}, clear=True):
            first = build_backend.create_metadata()
            second = build_backend.create_metadata()
        decoded = json.loads(base64.b64decode(build_backend.encode_metadata(first)))
        self.assertEqual(decoded, first)
        self.assertEqual([item["sha"] for item in first["git"]["commits"]], [sha, second_sha])
        self.assertEqual(first["git"]["commits"][0]["message"], message)
        self.assertTrue(first["git"]["dirty"])
        self.assertNotEqual(first["version"], second["version"])
        self.assertTrue(first["builtAtShanghai"].endswith("+08:00"))
        self.assertTrue(first["builtAtUtc"].endswith("+00:00"))


if __name__ == "__main__":
    unittest.main()
