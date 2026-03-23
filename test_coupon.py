import urllib.request
import json
url = "https://sportiveapi-production.up.railway.app/api/coupons/validate"
data = json.dumps({"code": "WELCOME", "orderTotal": 200}).encode("utf-8")
req = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json"})
try:
    with urllib.request.urlopen(req) as response:
        print(response.read().decode())
except Exception as e:
    print(e)
