import json
import os
import sys
import urllib.request
import urllib.error

TOKEN = os.environ.get("GITHUB_TOKEN")
if not TOKEN:
    print("Error: GITHUB_TOKEN environment variable not set")
    sys.exit(1)

REPO = "Volfheim/SmartAudioSwitcher"
TAG = "v3.0.5"

headers = {
    "Authorization": f"token {TOKEN}",
    "Accept": "application/vnd.github+json",
}

def request(url, method="GET", data=None, content_type="application/json"):
    if data is not None and content_type == "application/json": 
        data = json.dumps(data).encode('utf-8')
    req = urllib.request.Request(url, data=data, headers={**headers, "Content-Type": content_type}, method=method)
    try:
        with urllib.request.urlopen(req) as f:
            if method == "DELETE": return None
            return json.load(f)
    except urllib.error.HTTPError as e:
        print(f"Request failed: {e.code} {e.reason}")
        try: print(e.read().decode())
        except: pass
        raise

# Delete old release if exists
try:
    rel = request(f"https://api.github.com/repos/{REPO}/releases/tags/{TAG}")
    print("Deleting old release...")
    request(f"https://api.github.com/repos/{REPO}/releases/{rel['id']}", method="DELETE")
except:
    pass

print(f"Creating release {TAG}...")
release_body = "### 🐛 Bug Fixes\n\n- Fixed an issue where the updater would prompt repeatedly because the update script failed to replace the file.\n- Modernized the update prompt UI, moving away from the standard Windows dialog.\n- Fixed an issue where the updater downloaded the incorrect executable variant in some cases."
release = request(
    f"https://api.github.com/repos/{REPO}/releases",
    method="POST",
    data={
        "tag_name": TAG,
        "name": "SmartAudioSwitcher",
        "body": release_body,
        "target_commitish": "master"
    }
)

def upload_asset(path, name):
    print(f"Uploading {name}...")
    upload_url = release['upload_url'].replace("{?name,label}", f"?name={name}")
    with open(path, 'rb') as f:
        file_content = f.read()
    req = urllib.request.Request(
        upload_url, 
        data=file_content, 
        headers={**headers, "Content-Type": "application/vnd.microsoft.portable-executable"}, 
        method="POST"
    )
    try:
        with urllib.request.urlopen(req): 
            print(f"Uploaded {name}!")
    except Exception as e:
        print(f"Upload failed: {e}")

upload_asset("SmartAudioSwitcher/bin/Publish/Light_SingleFile/SmartAudioSwitcher.exe", "SmartAudioSwitcher.exe")
upload_asset("SmartAudioSwitcher/bin/Publish/Full_SelfContained/SmartAudioSwitcher.exe", "SmartAudioSwitcher_Full.exe")
print("Done!")
