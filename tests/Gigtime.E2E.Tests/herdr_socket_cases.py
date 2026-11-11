"""Protocol cases driven through the executable; every socket lives in a private root."""
import contextlib
import json
import io
import os
from pathlib import Path
import pwd
import runpy
import signal
import socket
import stat
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest import mock

SCRIPT = Path(sys.argv.pop(1)).absolute()


def status(instance=None):
	return {"ok": True, "command": "status", "data": {"instance": instance,
		"running": {"client": {"key": "acme"}, "project": {"key": "work"},
			"raw_seconds": 3661, "display": "01:02:00"}, "paused": []}}


def framed(body, framing="length", code=200):
	if isinstance(body, dict):
		body = json.dumps(body).encode()
	head = f"HTTP/1.1 {code} Test\r\n".encode()
	if framing == "length":
		return head + f"Content-Length: {len(body)}\r\n\r\n".encode() + body
	if framing == "chunked":
		chunks = [body[i:i+17] for i in range(0, len(body), 17)]
		return head + b"Transfer-Encoding: chunked\r\n\r\n" + b"".join(
			f"{len(chunk):x}\r\n".encode() + chunk + b"\r\n" for chunk in chunks) + b"0\r\n\r\n"
	return head + b"Connection: close\r\n\r\n" + body


class SocketCases(unittest.TestCase):
	def setUp(self):
		self.root_context = tempfile.TemporaryDirectory(prefix="gig-herdr-", dir="/tmp")
		self.addCleanup(self.root_context.cleanup)
		self.root = Path(self.root_context.name)
		self.env = dict(os.environ)
		for key in ("GIGTIME_INSTANCE", "LISTEN_PID", "LISTEN_FDS", "LISTEN_FDNAMES", "DBUS_SESSION_BUS_ADDRESS"):
			self.env.pop(key, None)
		for key in ("HOME", "TMPDIR", "XDG_DATA_HOME", "XDG_CONFIG_HOME", "XDG_RUNTIME_DIR", "XDG_STATE_HOME", "XDG_CACHE_HOME"):
			path = self.root / key
			path.mkdir()
			self.env[key] = str(path)
		self.path = Path(self.env["XDG_RUNTIME_DIR"]) / "gigtime/gigtimed.sock"
		self.path.parent.mkdir()

	def invoke(self, timeout=4):
		return subprocess.run([str(SCRIPT)], cwd=self.root, env=self.env,
			capture_output=True, text=True, timeout=timeout)

	def failure(self, result):
		self.assertEqual(1, result.returncode, result)
		self.assertEqual("", result.stdout)
		self.assertTrue(result.stderr.startswith("gig-tab-status: "), result.stderr)
		self.assertEqual(1, result.stderr.count("\n"), result.stderr)
		self.assertNotIn("Traceback", result.stderr)
		self.assertNotIn("PRIVATE_BODY", result.stderr)

	@contextlib.contextmanager
	def peer(self, response=None, mode=None, path=None):
		path = path or self.path
		listener = socket.socket(socket.AF_UNIX)
		listener.bind(str(path))
		listener.listen()
		listener.settimeout(0.05)
		stop = threading.Event()
		requests = []
		errors = []

		def serve():
			try:
				while not stop.is_set():
					try:
						client, _ = listener.accept()
						break
					except TimeoutError:
						continue
				else:
					return
				with client:
					client.settimeout(0.2)
					request = b""
					while b"\r\n\r\n" not in request and not stop.is_set():
						request += client.recv(4096)
					requests.append(request)
					if mode == "silent":
						stop.wait(5)
					elif mode in ("headers", "body", "chunked body"):
						if mode == "body":
							client.sendall(b"HTTP/1.1 200 OK\r\nContent-Length: 999\r\n\r\n")
						elif mode == "chunked body":
							client.sendall(b"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n100\r\n")
						for byte in (b"HTTP/1.1 200 OK\r\nX-Drip: " + b"x" * 100):
							client.sendall(bytes([byte]))
							if stop.wait(0.1):
								break
					else:
						# Multiple writes exercise parser reads independently of packet boundaries.
						for offset in range(0, len(response), 8192):
							client.sendall(response[offset:offset+8192])
			except (BrokenPipeError, ConnectionResetError):
				pass
			except Exception as error:
				errors.append(error)

		thread = threading.Thread(target=serve)
		thread.start()
		try:
			yield requests
		finally:
			stop.set()
			thread.join(2)
			listener.close()
			path.unlink()
			self.assertFalse(thread.is_alive())
			self.assertEqual([], errors)

	def test_supported_framing_and_bodyless_request(self):
		self.env.update(HTTP_PROXY="http://127.0.0.1:1", HTTPS_PROXY="http://127.0.0.1:1", ALL_PROXY="http://127.0.0.1:1")
		for framing in ("length", "chunked", "close"):
			with self.subTest(framing=framing), self.peer(framed(status(), framing)) as requests:
				result = self.invoke()
				self.assertEqual(0, result.returncode, result.stderr)
				self.assertEqual("acme/work 01:01:01\n", result.stdout)
				self.assertEqual("", result.stderr)
				self.assertEqual(b"GET /v1/status HTTP/1.1", requests[0].split(b"\r\n")[0])
				self.assertIn(b"\r\nHost: localhost\r\n", requests[0])
				self.assertIn(b"\r\nConnection: close\r\n", requests[0])
				self.assertNotIn(b"Content-Length", requests[0])
				self.assertTrue(requests[0].endswith(b"\r\n\r\n"))

	def test_non_200_is_never_a_success(self):
		for code in (201, 302, 404, 500):
			with self.subTest(code=code), self.peer(framed(status(), code=code)):
				self.failure(self.invoke())

	def test_instance_is_checked_even_with_two_different_instance_payloads(self):
		self.env["GIGTIME_INSTANCE"] = "dev"
		self.path = self.path.parent.parent / "gigtime-dev/gigtimed.sock"
		self.path.parent.mkdir()
		wrong = status()
		wrong["data"]["running"]["client"]["key"] = "default"
		with self.peer(framed(wrong)):
			self.failure(self.invoke())
		with self.peer(framed(status("dev"))):
			self.assertEqual("acme/work 01:01:01\n", self.invoke().stdout)

	def test_failure_message_survives_without_success_command(self):
		with self.peer(framed({"ok": False, "command": "request", "error": {
			"message": "service unavailable\ntry again"}}, code=500)):
			result = self.invoke()
			self.failure(result)
			self.assertEqual("gig-tab-status: service unavailable try again\n", result.stderr)

	def test_malformed_http_and_envelopes_never_echo_body(self):
		bodies = [b"null", b"[]", b"PRIVATE_BODY", b"<html>PRIVATE_BODY</html>", b"{}"]
		for key, value in [("ok", 1), ("ok", "true"), ("command", "show"), ("data", None)]:
			item = status()
			item[key] = value
			bodies.append(json.dumps(item).encode())
		for key, value in [("running", {}), ("paused", {}), ("instance", "wrong")]:
			item = status()
			item["data"][key] = value
			bodies.append(json.dumps(item).encode())
		for key in ("running", "paused", "instance"):
			item = status()
			del item["data"][key]
			bodies.append(json.dumps(item).encode())
		responses = [framed(body) for body in bodies] + [
			b"PRIVATE_BODY\r\n\r\n",
			b"HTTP/1.1 200 OK\r\nX: " + b"x" * 70000 + b"\r\n\r\n",
			b"HTTP/1.1 200 OK\r\n" + b"X: x\r\n" * 101 + b"\r\n",
			b"HTTP/1.1 200 OK\r\nContent-Length: 500\r\n\r\n{}",
			b"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\nZZ\r\nPRIVATE_BODY",
			b"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nx",
		]
		body = json.dumps(status()).encode()
		for header in (b"Content-Length: nonsense", b"Content-Length: -1", b"Invalid header",
			b"Content-Length: 1\r\nContent-Length: 1", b"Transfer-Encoding: gzip",
			b"Transfer-Encoding: chunked\r\nContent-Length: 0", b"Content-Encoding: gzip"):
			responses.append(b"HTTP/1.1 200 OK\r\n" + header + b"\r\n\r\n" + body)

		for index, response in enumerate(responses):
			with self.subTest(case=index), self.peer(response):
				self.failure(self.invoke())

	def test_valid_envelope_with_truncated_content_length_is_rejected(self):
		body = json.dumps(status()).encode()
		with self.peer(b"HTTP/1.1 200 OK\r\nContent-Length: " + str(len(body) + 10).encode() + b"\r\n\r\n" + body):
			result = self.invoke()
			self.failure(result)
			self.assertEqual("gig-tab-status: incomplete status response\n", result.stderr)

	def test_malformed_chunk_boundaries_are_rejected_even_with_valid_json(self):
		body = json.dumps(status()).encode()
		head = b"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n"
		size = f"{len(body):x}".encode()
		for name, chunks in (
			("corrupt data delimiter", size + b"\r\n" + body + b"XX0\r\n\r\n"),
			("missing final line", size + b"\r\n" + body + b"\r\n0\r\n"),
			("missing line after trailer", size + b"\r\n" + body + b"\r\n0\r\nX-Check: ok\r\n"),
			("bare LF on size", size + b"\n" + body + b"\r\n0\r\n\r\n"),
			("bare LF on final line", size + b"\r\n" + body + b"\r\n0\r\n\n"),
			("signed chunk size", b"+" + size + b"\r\n" + body + b"\r\n0\r\n\r\n"),
			("unclosed extension quote", size + b';tag="bad\r\n' + body + b"\r\n0\r\n\r\n"),
			("invalid trailer", size + b"\r\n" + body + b"\r\n0\r\nmissing-colon\r\n\r\n"),
			("too many trailers", size + b"\r\n" + body + b"\r\n0\r\n" + b"X-Check: ok\r\n" * 101 + b"\r\n"),
		):
			with self.subTest(case=name), self.peer(head + chunks):
				result = self.invoke()
				self.failure(result)
				self.assertEqual("gig-tab-status: invalid HTTP status response\n", result.stderr)

	def test_chunk_extensions_and_trailers_are_ignored_after_validation(self):
		body = json.dumps(status()).encode()
		response = (
			b"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n"
			+ f"{len(body):X}".encode() + b' ; flag; tag = "a;\\"b"; x=value\r\n'
			+ body + b"\r\n00;last=yes\r\nX-Check: ok\r\nX-Empty:\r\n\r\n"
		)
		with self.peer(response):
			result = self.invoke()
			self.assertEqual(0, result.returncode, result.stderr)
			self.assertEqual("acme/work 01:01:01\n", result.stdout)
			self.assertEqual("", result.stderr)

	def test_alarm_at_cleanup_boundary_still_closes_and_restores(self):
		module = runpy.run_path(str(SCRIPT))
		for boundary in ("ignore", "cancel"):
			with self.subTest(boundary=boundary):
				response = mock.Mock(status=200, length=0)
				response.getheader.return_value = "identity"
				response.headers.defects = []
				response.headers.get_all.return_value = []
				response.read.return_value = b""
				connection = mock.Mock()
				connection.getresponse.return_value = response
				previous = object()

				def handler(sig, value):
					if boundary == "ignore" and value == signal.SIG_IGN:
						raise TimeoutError("status deadline exceeded")
					return previous

				def timer(which, value):
					if boundary == "cancel" and value == 0:
						raise TimeoutError("status deadline exceeded")

				with mock.patch.dict(module["read_status"].__globals__, UnixStatusConnection=mock.Mock(return_value=connection)), \
					mock.patch("signal.signal", side_effect=handler) as signals, \
					mock.patch("signal.setitimer", side_effect=timer), \
					mock.patch("json.loads", return_value=status()):
					with mock.patch.dict(module["main"].__globals__, socket_path=lambda: ("unused", None)), \
						contextlib.redirect_stdout(io.StringIO()) as stdout, contextlib.redirect_stderr(io.StringIO()) as stderr:
						self.assertEqual(1, module["main"]())
					self.assertEqual("", stdout.getvalue())
					self.assertEqual("gig-tab-status: status deadline exceeded\n", stderr.getvalue())
					response.close.assert_called_once()
					connection.close.assert_called_once()
					self.assertEqual(mock.call(signal.SIGALRM, previous), signals.call_args)

	def test_cap_applies_to_all_body_framings(self):
		# A valid JSON envelope beyond the cap must fail even with unknown length.
		item = status()
		item["padding"] = "x" * 1048576
		for framing in ("length", "chunked", "close"):
			with self.subTest(framing=framing), self.peer(framed(item, framing)):
				self.failure(self.invoke())

	def test_recursion_error_really_reaches_the_catch(self):
		# Python 3.14's parser can exceed sys.getrecursionlimit: probe the actual decoder.
		depth = 1000
		while True:
			body = b"[" * depth + b"]" * depth
			try:
				json.loads(body)
			except RecursionError:
				break
			depth *= 2
			self.assertLess(depth, 262144)
		with self.peer(framed(body)):
			result = self.invoke()
			self.failure(result)
			self.assertEqual("gig-tab-status: status JSON is nested too deeply\n", result.stderr)

	def test_silent_headers_and_body_drips_have_one_total_deadline(self):
		for mode in ("silent", "headers", "body", "chunked body"):
			with self.subTest(mode=mode), self.peer(mode=mode):
				start = time.monotonic()
				result = self.invoke(timeout=3.5)
				elapsed = time.monotonic() - start
				self.failure(result)
				self.assertLess(elapsed, 2.0)
				print(f"deadline {mode}: {elapsed:.3f}s", file=sys.stderr)
				self.assertGreater(elapsed, 1.3)

	def test_format_boundaries_and_invalid_fields(self):
		for seconds, expected in [(0, "00:00:00"), (59, "00:00:59"), (3599, "00:59:59"),
			(3600, "01:00:00"), (3661, "01:01:01"), (360000, "100:00:00")]:
			for paused in (False, True):
				item = status()
				item["data"]["running"]["raw_seconds"] = seconds
				if paused:
					item["data"]["paused"] = [item["data"]["running"]]
					item["data"]["running"] = None
				with self.subTest(seconds=seconds, paused=paused), self.peer(framed(item)):
					result = self.invoke()
					self.assertEqual(0, result.returncode, result.stderr)
					self.assertEqual("acme/work " + ("paused " if paused else "") + expected + "\n", result.stdout)
		for field, values in [("raw_seconds", [None, "3661", True, 1.5, -1]),
			("client", [42, "", "acme\u0080"]), ("project", [42, "", "work\u0085"])]:
			for value in values:
				item = status()
				task = item["data"]["running"]
				if field == "raw_seconds":
					if value is None:
						del task[field]
					else:
						task[field] = value
				else:
					task[field]["key"] = value
				with self.subTest(field=field, value=value), self.peer(framed(item)):
					self.failure(self.invoke())

	def test_no_gig_discovery_and_executable_import_isolation(self):
		bin_path = self.root / "bin"
		bin_path.mkdir()
		(bin_path / "python3").symlink_to(sys.executable)
		self.env["PATH"] = str(bin_path)
		with self.peer(framed(status())):
			self.assertEqual(0, self.invoke().returncode)
		marker = self.root / "planted"
		planted = "#!/bin/sh\nprintf planted > '" + str(marker) + "'\n"
		for path in (self.root / "gig", bin_path / "gig", self.root / "relative/.local/bin/gig"):
			path.parent.mkdir(parents=True, exist_ok=True)
			path.write_text(planted)
			path.chmod(0o700)
		self.env.update(HOME="relative", PYTHONPATH=str(self.root), PYTHONHOME="invalid",
			PATH=".:" + str(bin_path))
		for module in ("json", "http", "sitecustomize"):
			(self.root / (module + ".py")).write_text(f"open({str(marker)!r}, 'w').write('imported')")
		with self.peer(framed(status())):
			result = self.invoke()
			self.assertEqual(0, result.returncode, result.stderr)
		self.assertFalse(marker.exists())

	def test_path_parity_with_published_platform_for_unset_relative_and_named_roots(self):
		cli = self.env["GIGTIME_TEST_CLI"]
		for instance in (None, "", "dev"):
			for runtime in (None, "", "relative", self.env["XDG_RUNTIME_DIR"]):
				for temporary in (self.env["TMPDIR"], "TMPDIR"):
					environment = dict(self.env, TMPDIR=temporary, USER="spoofed", LOGNAME="spoofed")
					for key, value in (("GIGTIME_INSTANCE", instance), ("XDG_RUNTIME_DIR", runtime)):
						if value is None:
							environment.pop(key, None)
						else:
							environment[key] = value
					with self.subTest(instance=instance, runtime=runtime, temporary=temporary):
						actual = subprocess.run([cli, "setup", "--check", "--json"], cwd=self.root,
							env=environment, capture_output=True, text=True, timeout=5)
						expected = json.loads(actual.stdout)["data"]["socket_path"]
						probe = subprocess.run([sys.executable, "-I", "-c",
							"import runpy,sys; print(runpy.run_path(sys.argv[1])['socket_path']()[0])", str(SCRIPT)],
							cwd=self.root, env=environment, capture_output=True, text=True, timeout=5)
						self.assertEqual(0, probe.returncode, probe.stderr)
						self.assertEqual(expected, probe.stdout.rstrip())

	def test_wrong_fallback_owner_is_rejected_before_connection(self):
		module = runpy.run_path(str(SCRIPT))
		self.env["XDG_RUNTIME_DIR"] = ""
		fallback = Path(self.env["TMPDIR"]) / ("gigtime-" + pwd.getpwuid(os.geteuid()).pw_name)
		fallback.mkdir(mode=0o700)
		actual = list(fallback.stat())
		actual[4] = os.geteuid() + 1
		with mock.patch.dict(os.environ, self.env, clear=True), mock.patch("os.lstat", return_value=os.stat_result(actual)), \
			mock.patch("socket.socket", side_effect=AssertionError("connection attempted")):
			with self.assertRaisesRegex(ValueError, "owned by the current user"):
				module["socket_path"]()

	def test_fallback_safety_is_read_only(self):
		self.env["XDG_RUNTIME_DIR"] = "relative"
		fallback = Path(self.env["TMPDIR"]) / ("gigtime-" + pwd.getpwuid(os.geteuid()).pw_name)
		cwd_socket = self.root / "relative/gigtime/gigtimed.sock"
		cwd_socket.parent.mkdir(parents=True)
		with self.peer(framed(status()), path=cwd_socket) as requests:
			self.failure(self.invoke())
			self.assertEqual([], requests)
		self.assertFalse(fallback.exists())
		for mode in (0o755, 0o1700, 0o770):
			fallback.mkdir()
			fallback.chmod(mode)
			child = fallback / "gigtime"
			child.mkdir()
			with self.peer(framed(status()), path=child / "gigtimed.sock") as requests:
				self.failure(self.invoke())
				self.assertEqual([], requests)
			child.rmdir()
			self.assertEqual(mode, stat.S_IMODE(fallback.stat().st_mode))
			fallback.rmdir()
		fallback.write_text("planted")
		self.failure(self.invoke())
		self.assertEqual("planted", fallback.read_text())
		fallback.unlink()
		fallback.symlink_to(self.path.parent, target_is_directory=True)
		self.failure(self.invoke())
		self.assertTrue(fallback.is_symlink())

	def test_instance_and_socket_symlinks_follow_platform_policy(self):
		real = self.root / "actual"
		real.mkdir()
		self.path.parent.rmdir()
		self.path.parent.symlink_to(real, target_is_directory=True)
		with self.peer(framed(status())):
			self.assertEqual(0, self.invoke().returncode)
		other = real / "other.sock"
		self.path.symlink_to(other)
		with self.peer(framed(status()), path=other):
			self.assertEqual(0, self.invoke().returncode)
		self.assertTrue(self.path.is_symlink())

	def test_missing_refused_and_invalid_instance_do_not_repair(self):
		result = self.invoke()
		self.failure(result)
		self.assertIn(f"gig-tab-status: Cannot reach gigtimed at {self.path}: ", result.stderr)
		self.assertIn("No such file or directory", result.stderr)
		with socket.socket(socket.AF_UNIX) as listener:
			listener.bind(str(self.path))
		result = self.invoke()
		self.failure(result)
		self.assertIn(f"gig-tab-status: Cannot reach gigtimed at {self.path}: ", result.stderr)
		self.assertIn("Connection refused", result.stderr)
		self.assertTrue(self.path.exists())
		self.path.unlink()
		module = runpy.run_path(str(SCRIPT))
		with mock.patch.dict(os.environ, self.env, clear=True), \
			mock.patch("socket.socket") as sockets, contextlib.redirect_stderr(io.StringIO()) as stderr:
			sockets.return_value.connect.side_effect = PermissionError("permission denied")
			self.assertEqual(1, module["main"]())
		self.assertEqual(f"gig-tab-status: Cannot reach gigtimed at {self.path}: permission denied\n", stderr.getvalue())
		for name in ("../dev", "DEV", "a\n", "-dev", "a" * 33):
			self.env["GIGTIME_INSTANCE"] = name
			with self.subTest(name=name), self.peer(framed(status())) as requests:
				self.failure(self.invoke())
				self.assertEqual([], requests)


if __name__ == "__main__":
	unittest.main(verbosity=2)
