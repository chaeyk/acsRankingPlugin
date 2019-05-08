# acsRankingPlugin
Assetto Corsa Server Ranking Plugin

# 실행하기
Assetto Corsa의 멀티 서버는 UDP socket을 열어 외부의 플러그인과 통신하는 기능을 제공하고 있다. acsRankingPlugin을 사용하려면 이 기능을 활성화해야 한다. 여기서는 플러그인이 9701 포트를 사용하고, 서버가 9702 포트를 사용하는 것으로 설명한다.

명령 프롬프트에서 다음과 같이 실행한다.

```
acsRankingPlugin.exe --server-port=9702 --plugin-port=9701
```

그리고 서버에 플러그인 설정한 후 실행해야 한다.

> **서버매니저 사용시**
>
> Advanced Options - Server Plugin 에서 Address에 127.0.0.1:9701, Local Port에 9702를 입력한다.

> **서버 직접 실행시**
>
> 다음을 설정 파일에 추가/수정하고 서버를 실행해야 한다.
>
> ```
> UDP_PLUGIN_ADDRESS=127.0.0.1:9701
> UDP_PLUGIN_LOCAL_PORT=9702
> ```

도움말은 ``--help`` 옵션으로 볼 수 있다.

```
acsRankingPlugin --help
```

데이터를 초기화하려면 ``--reset`` 옵션을 사용해서 실행한다.

```
acsRankingPlugin.exe --server-port=9702 --plugin-port=9701 --reset
```


# 특징
* 실행 순서는 서버를 먼저 시작해도 되고 플러그인을 먼저 시작해도 된다.
* 중간에 서버나 플러그인을 종료했다가 재시작해도 데이터는 이어서 연동이 된다.
* 플러그인이 종료된 상태에서는 랩타임 기록이 등록되지 않는다.
* Admin 권한이 없어도 자신의 ballast를 조정할 수 있다.
* UDP 포트만 다르게 지정하면 플러그인을 여러개 띄워서 사용할 수 있다.
* 서킷이 변경되면 현재까지의 데이터를 모두 삭제하고 새로 시작한다.
