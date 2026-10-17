set -e
zmodload zsh/zpty

first_tab() {
  local typed=$1 expected=$2 reply
  zpty completion zsh -f
  {
    # The test owns its temporary fpath, which compaudit otherwise rejects under /tmp.
    zpty -w completion "fpath=(${(q)completion_directory} \$fpath); autoload -Uz compinit; compinit -D -u; PS1=''; function capture() { zle -I; print -r -- \"RESULT:\$BUFFER:END\"; BUFFER=; CURSOR=0; zle redisplay; }; zle -N capture; bindkey '^X' capture; print -r -- 'COMPLETE''READY'"
    zpty -r -m completion reply '*COMPLETEREADY*'
    zpty -w -n completion "$typed"$'\t\x18'
    zpty -r -m completion reply '*:END*'
    if [[ $reply != *"RESULT:$expected:END"* ]]; then
      print -u2 -r -- "First TAB failed for $typed: $reply"
      return 1
    fi
    print -r -- "RESULT:$expected:END"
  } always {
    zpty -d completion
  }
}

completion_directory=$1
first_tab 'gig star' 'gig start '
first_tab 'gig report --ran' 'gig report --range '
first_tab 'gig start ac' 'gig start acme '
first_tab 'gig start acme wo' 'gig start acme work '
